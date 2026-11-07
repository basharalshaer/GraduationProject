using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Models.Entities;

public class OfficeAccountRequest
{
    public Guid Id { get; set; }
    public string ReferenceNo { get; set; } = string.Empty;
    public string OfficeName { get; set; } = string.Empty;
    public string CommercialRegNo { get; set; } = string.Empty;
    public string LicenseNo { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Description { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public string? AdminNotes { get; set; }

    public Guid? ReviewedByUserId { get; set; }
    public User? ReviewedByUser { get; set; }
    public DateTime? ReviewedAt { get; set; }

    public Guid? PreviousRequestId { get; set; }          // set when re-applying after a rejection
    public OfficeAccountRequest? PreviousRequest { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<RequestDocument> Documents { get; set; } = new List<RequestDocument>();
}