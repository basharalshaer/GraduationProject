using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Models.Entities;
using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Services;

public enum OtpResult { Success, Invalid, TooManyAttempts }

public interface IOtpService
{
    /// Adds a new OTP token (does NOT save) and returns the 6-digit code.
    /// Returns null when respectCooldown is true and a code was sent less than 60 seconds ago.
    Task<string?> CreateAsync(User user, bool respectCooldown = false);

    /// Checks the code. On success it marks the token as used but does NOT save (the caller saves).
    Task<OtpResult> VerifyAsync(User user, string code);
}

public class OtpService : IOtpService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);
    private const int MaxAttempts = 5;

    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public OtpService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<string?> CreateAsync(User user, bool respectCooldown = false)
    {
        var active = await _db.AuthTokens
            .Where(t => t.UserId == user.Id && t.Type == TokenType.EmailOtp && t.UsedAt == null)
            .ToListAsync();

        if (respectCooldown && active.Any(t => t.CreatedAt > DateTime.UtcNow - Cooldown))
            return null;

        foreach (var t in active) t.UsedAt = DateTime.UtcNow;   // a new code cancels the old one

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        _db.AuthTokens.Add(new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Type = TokenType.EmailOtp,
            TokenHash = Hash(user.Id, code),
            ExpiresAt = DateTime.UtcNow.Add(Lifetime),
            CreatedAt = DateTime.UtcNow
        });

        return code;
    }

    public async Task<OtpResult> VerifyAsync(User user, string code)
    {
        var token = await _db.AuthTokens
            .Where(t => t.UserId == user.Id && t.Type == TokenType.EmailOtp
                     && t.UsedAt == null && t.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync();

        if (token is null) return OtpResult.Invalid;
        if (token.FailedAttempts >= MaxAttempts) return OtpResult.TooManyAttempts;

        var expected = Encoding.UTF8.GetBytes(Hash(user.Id, code));
        var actual = Encoding.UTF8.GetBytes(token.TokenHash);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            token.FailedAttempts++;
            await _db.SaveChangesAsync();
            return OtpResult.Invalid;
        }

        token.UsedAt = DateTime.UtcNow;
        return OtpResult.Success;
    }

    // A 6-digit code has only 1 million possibilities, so a plain hash could be reversed instantly.
    // HMAC with a secret key (here the JWT key) stops that.
    private string Hash(Guid userId, string code)
    {
        var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"]!);
        return Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{userId}:{code}")));
    }
}