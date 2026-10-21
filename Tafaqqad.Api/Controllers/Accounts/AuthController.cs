using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Dtos;
using Tafaqqad.Api.Models.Entities;
using Tafaqqad.Api.Models.Enums;
using Tafaqqad.Api.Services;
using Microsoft.AspNetCore.Authorization;

namespace Tafaqqad.Api.Controllers.Accounts;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly PasswordHasher<User> _hasher = new();

    public AuthController(AppDbContext db, IJwtTokenService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

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
            Role = UserRole.Client,          // register is always a client
            Email = email,
            FullName = req.FullName.Trim(),
            Phone = req.Phone?.Trim(),
            Status = UserStatus.Active
        };
        user.PasswordHash = _hasher.HashPassword(user, req.Password);

        _db.Users.Add(user);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // two requests with the same email at the same moment: the unique index caught it
            return Conflict(new { message = "This email is already registered." });
        }

        return StatusCode(StatusCodes.Status201Created,
            new { user.Id, message = "Account created. You can log in now." });
    }

    // POST api/auth/login  (all roles)
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        // same answer for "no such user" and "wrong password" so attackers learn nothing
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
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "This account is not activated yet. Use the link sent to your email." });

        return Ok(new AuthResponse
        {
            Token = _jwt.CreateToken(user),
            User = new AuthUserDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role.ToString()
            }
        });
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
}