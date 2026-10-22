using System.ComponentModel.DataAnnotations;

namespace Tafaqqad.Api.Dtos;

public class RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).{8,100}$",
        ErrorMessage = "Password must be 8+ characters with at least one letter and one number.")]
    public string Password { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? Phone { get; set; }
}

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class AuthUserDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public AuthUserDto User { get; set; } = new();
}

public class EmailRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class VerifyOtpRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "The code must be 6 digits.")]
    public string Code { get; set; } = string.Empty;
}

public class TokenPasswordRequest
{
    [Required]
    public string Token { get; set; } = string.Empty;

    [Required, RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).{8,100}$",
        ErrorMessage = "Password must be 8+ characters with at least one letter and one number.")]
    public string Password { get; set; } = string.Empty;
}