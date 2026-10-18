using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Models.Entities;

public class AuthToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public TokenType Type { get; set; }
    public string TokenHash { get; set; } = string.Empty;   // never store the raw token
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
}