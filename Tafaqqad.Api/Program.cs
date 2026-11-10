using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Services;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ---- Fail fast if required settings are missing or unsafe ----
var config = builder.Configuration;
var configProblems = new List<string>();

var jwtKey = config["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    configProblems.Add("Jwt:Key is missing or shorter than 32 characters.");
if (string.IsNullOrWhiteSpace(config["Jwt:Issuer"]))
    configProblems.Add("Jwt:Issuer is missing.");
if (string.IsNullOrWhiteSpace(config["Jwt:Audience"]))
    configProblems.Add("Jwt:Audience is missing.");
if (string.IsNullOrWhiteSpace(config.GetConnectionString("Default")))
    configProblems.Add("ConnectionStrings:Default is missing.");
if (!Uri.TryCreate(config["App:FrontendBaseUrl"], UriKind.Absolute, out _))
    configProblems.Add("App:FrontendBaseUrl is missing or not a full URL (e.g. http://localhost:5173).");

var allowedOrigins = config.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
if (allowedOrigins.Length == 0)
    configProblems.Add("Cors:Origins is empty.");

if (configProblems.Count > 0)
    throw new InvalidOperationException(
        "Tafaqqad cannot start because of configuration problems:\n - " + string.Join("\n - ", configProblems));

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("Retry-After"));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the token from /api/auth/login (without the word Bearer)."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthTokenService, AuthTokenService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<OfficeStatusService>();
builder.Services.AddSingleton<IAppEmailSender, ConsoleEmailSender>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();

builder.Services.AddExceptionHandler<Tafaqqad.Api.Infrastructure.GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, ct) =>
    {
        int? retryAfter = null;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait))
        {
            retryAfter = (int)Math.Ceiling(wait.TotalSeconds);
            context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();
        }
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            message = "Too many attempts. Please wait and try again.",
            retryAfterSeconds = retryAfter
        }, ct);
    };

    // Backstop for every endpoint: 300 requests per minute per IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));

    // Endpoints that check a secret (password, OTP, token)
    options.AddPolicy("login", http =>
        RateLimitPartition.GetSlidingWindowLimiter(ClientIp(http), _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
            SegmentsPerWindow = 5,
            QueueLimit = 0
        }));

    // Endpoints that send an email
    options.AddPolicy("send-code", http =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0
        }));

    // Public forms with file uploads
    options.AddPolicy("public-form", http =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0
        }));
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            NameClaimType = "name",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseExceptionHandler();

// Give empty-body errors (404 unknown route, 401, 403, 405...) a JSON body the frontend can read
app.UseStatusCodePages(async statusContext =>
{
    var http = statusContext.HttpContext;
    var message = http.Response.StatusCode switch
    {
        401 => "You need to log in.",
        403 => "You don't have permission to do this.",
        404 => "Not found.",
        405 => "This method is not allowed here.",
        415 => "Unsupported content type.",
        429 => "Too many requests. Please wait and try again.",
        _ => "The request failed."
    };
    await http.Response.WriteAsJsonAsync(new
    {
        message,
        traceId = System.Diagnostics.Activity.Current?.Id ?? http.TraceIdentifier
    });
});

// Security headers on every response (added just before the response is sent,
// so they survive the error handler too)
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";

        // Strict CSP only for JSON - Swagger's HTML page and images are left alone
        if (context.Response.ContentType?.StartsWith("application/json") == true)
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

        return Task.CompletedTask;
    });
    await next();
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db, app.Configuration);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.Run();

static string ClientIp(HttpContext http) =>
    http.Connection.RemoteIpAddress?.ToString() ?? "unknown";