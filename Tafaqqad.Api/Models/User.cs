using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Models.Entities;

public class User
{
    public Guid Id { get; set; }
    public UserRole Role { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }   // null until the user sets a password
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateTime? EmailVerifiedAt { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AuthToken> AuthTokens { get; set; } = new List<AuthToken>();
}