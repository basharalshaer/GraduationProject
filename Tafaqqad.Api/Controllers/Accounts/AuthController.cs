using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Dtos;
using Tafaqqad.Api.Models.Entities;
using Tafaqqad.Api.Models.Enums;
using Tafaqqad.Api.Services;

namespace Tafaqqad.Api.Controllers.Accounts;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IAuthTokenService _tokens;
    private readonly IOtpService _otp;
    private readonly IAppEmailSender _email;
    private readonly IConfiguration _config;
    private readonly PasswordHasher<User> _hasher = new();

    public AuthController(AppDbContext db, IJwtTokenService jwt, IAuthTokenService tokens,
                          IOtpService otp, IAppEmailSender email, IConfiguration config)
    {
        _db = db;
        _jwt = jwt;
        _tokens = tokens;
        _otp = otp;
        _email = email;
        _config = config;
    }

    private string Link(string path, string token) =>
        $"{_config["App:FrontendBaseUrl"]!.TrimEnd('/')}/{path}?token={Uri.EscapeDataString(token)}";

    private AuthResponse BuildAuthResponse(User user) => new()
    {
        Token = _jwt.CreateToken(user),
        User = new AuthUserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role.ToString()
        }
    };

    // POST api/auth/register  (clients only)
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();

        if (await _db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { message = "This email is already registered." });

        var user = new User
        {
            Id = Guid.NewGuid(),
            Role = UserRole.Client,
            Email = email,
            FullName = req.FullName.Trim(),
            Phone = req.Phone?.Trim(),
            Status = UserStatus.PendingActivation      // becomes active after the OTP is verified
        };
        user.PasswordHash = _hasher.HashPassword(user, req.Password);
        _db.Users.Add(user);

        var code = (await _otp.CreateAsync(user))!;

        try
        {
            await _db.SaveChangesAsync();              // user + code saved together
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "This email is already registered." });
        }

        await _email.SendAsync(user.Email, "رمز التحقق من تفقّد",
            $"أهلاً {user.FullName}،{Environment.NewLine}رمز التحقق الخاص بك هو: {code}{Environment.NewLine}الرمز صالح لمدة 10 دقائق.");

        return StatusCode(StatusCodes.Status201Created,
            new { user.Id, user.Email, message = "Account created. Enter the 6-digit code we emailed you." });
    }

    // POST api/auth/verify-otp  (client enters the 6-digit code)
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp(VerifyOtpRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.Role == UserRole.Client);

        if (user is null || user.Status != UserStatus.PendingActivation)
            return BadRequest(new { message = "Invalid or expired code." });

        var result = await _otp.VerifyAsync(user, req.Code);

        if (result == OtpResult.TooManyAttempts)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = "Too many wrong attempts. Please request a new code." });

        if (result != OtpResult.Success)
            return BadRequest(new { message = "Invalid or expired code." });

        user.Status = UserStatus.Active;
        user.EmailVerifiedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();                  // saves the user change and the used code

        return Ok(BuildAuthResponse(user));            // logged in right after verification
    }

    // POST api/auth/resend-otp
    [HttpPost("resend-otp")]
    public async Task<IActionResult> ResendOtp(EmailRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user is not null && user.Role == UserRole.Client && user.Status == UserStatus.PendingActivation)
        {
            var code = await _otp.CreateAsync(user, respectCooldown: true);
            if (code is not null)
            {
                await _db.SaveChangesAsync();
                await _email.SendAsync(user.Email, "رمز التحقق من تفقّد",
                    $"رمز التحقق الجديد هو: {code}{Environment.NewLine}الرمز صالح لمدة 10 دقائق.");
            }
        }

        // always the same answer, so nobody can discover which emails exist
        return Ok(new { message = "If the account is waiting for verification, a new code was sent." });
    }

    // POST api/auth/login  (all roles)
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        // same answer for "no such user" and "wrong password"
        if (user is null || string.IsNullOrEmpty(user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password." });

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
        if (result == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Invalid email or password." });

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.HashPassword(user, req.Password);
            await _db.SaveChangesAsync();
        }

        if (user.Status == UserStatus.Suspended)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "This account is suspended. Please contact support." });

        if (user.Status == UserStatus.PendingActivation)
            return StatusCode(StatusCodes.Status403Forbidden, user.Role == UserRole.Client
                ? new { code = "EMAIL_NOT_VERIFIED", message = "Please verify your email with the 6-digit code we sent you." }
                : new { code = "NOT_ACTIVATED", message = "This account is not activated yet. Use the link sent to your email." });

        return Ok(BuildAuthResponse(user));
    }

    // GET api/auth/me  (any logged-in user)
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var id = HttpContext.User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(id, out var userId))
            return Unauthorized();

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null || user.Status != UserStatus.Active)
            return Unauthorized();

        return Ok(new AuthUserDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role.ToString()
        });
    }

    // TEMPORARY: only to test role protection. Delete it when we build the real admin endpoints.
    [Authorize(Roles = "Admin")]
    [HttpGet("admin-ping")]
    public IActionResult AdminPing() => Ok(new { message = "You are an admin." });

    // POST api/auth/set-password  (office and engineer activation link)
    [HttpPost("set-password")]
    public async Task<IActionResult> SetPassword(TokenPasswordRequest req)
    {
        var t = await _tokens.ValidateAsync(req.Token, TokenType.SetPassword);
        if (t is null)
            return BadRequest(new { message = "This link is invalid or has expired." });

        var user = t.User;
        if (user.Status == UserStatus.Suspended)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This account is suspended." });

        user.PasswordHash = _hasher.HashPassword(user, req.Password);
        user.Status = UserStatus.Active;
        user.EmailVerifiedAt ??= DateTime.UtcNow;
        t.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Password saved. You can log in now." });
    }

    // POST api/auth/forgot-password
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(EmailRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user is not null && user.Status == UserStatus.Active)
        {
            var raw = await _tokens.CreateAsync(user, TokenType.ResetPassword, TimeSpan.FromHours(1));
            await _db.SaveChangesAsync();
            await _email.SendAsync(user.Email, "إعادة تعيين كلمة المرور",
                $"لإعادة تعيين كلمة المرور استخدم الرابط التالي (صالح ساعة واحدة):{Environment.NewLine}{Link("reset-password", raw)}");
        }

        return Ok(new { message = "If the email exists, a reset link was sent." });
    }

    // POST api/auth/reset-password
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(TokenPasswordRequest req)
    {
        var t = await _tokens.ValidateAsync(req.Token, TokenType.ResetPassword);
        if (t is null || t.User.Status != UserStatus.Active)
            return BadRequest(new { message = "This link is invalid or has expired." });

        t.User.PasswordHash = _hasher.HashPassword(t.User, req.Password);
        t.UsedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Password changed. You can log in now." });
    }
}