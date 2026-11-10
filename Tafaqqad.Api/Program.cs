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

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>())
        .AllowAnyHeader()
        .AllowAnyMethod());
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

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.Run();

static string ClientIp(HttpContext http) =>
    http.Connection.RemoteIpAddress?.ToString() ?? "unknown";