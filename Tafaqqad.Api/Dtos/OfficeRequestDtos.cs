using System.ComponentModel.DataAnnotations;

namespace Tafaqqad.Api.Dtos;

public class SubmitOfficeRequestForm
{
    [Required, MaxLength(200)] public string OfficeName { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string CommercialRegNo { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string LicenseNo { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string City { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string ContactName { get; set; } = string.Empty;
    [Required, EmailAddress, MaxLength(256)] public string Email { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Phone { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Description { get; set; }

    /// Only when re-applying after a rejection
    [MaxLength(30)] public string? PreviousReferenceNo { get; set; }

    [Required] public IFormFile LicenseFile { get; set; } = null!;
    [Required] public IFormFile CommercialRegFile { get; set; } = null!;
    public IFormFile? LogoFile { get; set; }

    public class TrackRequest
    {
        [Required, MaxLength(30)]
        public string ReferenceNo { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(256)]
        public string Email { get; set; } = string.Empty;
    }

    public class TrackResponse
    {
        public string ReferenceNo { get; set; } = string.Empty;
        public string OfficeName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime SubmittedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? Message { get; set; }
        public bool CanReapply { get; set; }
    }
}