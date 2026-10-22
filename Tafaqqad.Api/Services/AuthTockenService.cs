using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Models.Entities;
using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Services;

public interface IAuthTokenService
{
    /// Creates a token, adds it to the context (does NOT save) and returns the raw token.
    Task<string> CreateAsync(User user, TokenType type, TimeSpan lifetime);

    /// Returns the token row (with its User) if the raw token is valid, unused and not expired.
    Task<AuthToken?> ValidateAsync(string rawToken, TokenType type);
}

public class AuthTokenService : IAuthTokenService
{
    private readonly AppDbContext _db;
    public AuthTokenService(AppDbContext db) => _db = db;

    public async Task<string> CreateAsync(User user, TokenType type, TimeSpan lifetime)
    {
        // A new link cancels any older unused link of the same type for this user
        var older = await _db.AuthTokens
            .Where(t => t.UserId == user.Id && t.Type == type && t.UsedAt == null)
            .ToListAsync();
        foreach (var t in older) t.UsedAt = DateTime.UtcNow;

        var raw = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        _db.AuthTokens.Add(new AuthToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Type = type,
            TokenHash = Hash(raw),            // only the hash is stored
            ExpiresAt = DateTime.UtcNow.Add(lifetime)
        });

        return raw;
    }

    public async Task<AuthToken?> ValidateAsync(string rawToken, TokenType type)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;

        var hash = Hash(rawToken.Trim());
        return await _db.AuthTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash
                                   && t.Type == type
                                   && t.UsedAt == null
                                   && t.ExpiresAt > DateTime.UtcNow);
    }

    private static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}