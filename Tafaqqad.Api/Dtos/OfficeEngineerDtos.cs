using System.ComponentModel.DataAnnotations;

namespace Tafaqqad.Api.Dtos;

public class CreateEngineerRequest
{
    [Required, MaxLength(150)] public string FullName { get; set; } = string.Empty;
    [Required, EmailAddress, MaxLength(200)] public string Email { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Phone { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string Specialty { get; set; } = string.Empty;      // إنشائي، معماري، كهرباء، ميكانيك
    [Range(0, 60)] public int YearsExperience { get; set; }
    [Required, MaxLength(30)] public string SyndicateNo { get; set; } = string.Empty;
}

public class EngineerDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string Specialty { get; set; } = string.Empty;
    public int YearsExperience { get; set; }
    public string SyndicateNo { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;       // Active, Deactivated
    public bool IsActivated { get; set; }                     // has the engineer set a password yet?
}

public class CreateEngineerResponse
{
    public EngineerDto Engineer { get; set; } = new();
    public bool EmailSent { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class UpdateEngineerRequest
{
    [Required, MaxLength(150)] public string FullName { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Phone { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string Specialty { get; set; } = string.Empty;
    [Range(0, 60)] public int YearsExperience { get; set; }
    [Required, MaxLength(30)] public string SyndicateNo { get; set; } = string.Empty;
}