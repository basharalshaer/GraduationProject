namespace Tafaqqad.Api.Dtos;

public class OfficeRequestListItem
{
    public Guid Id { get; set; }
    public string ReferenceNo { get; set; } = string.Empty;
    public string OfficeName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string LicenseNo { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }
    public bool IsReapplication { get; set; }
}

public class OfficeRequestListResponse
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public Dictionary<string, int> Counts { get; set; } = new();   // for the tab numbers
    public List<OfficeRequestListItem> Items { get; set; } = new();
}

public class RequestDocumentDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}

public class AutoChecksDto
{
    public bool LicenseNotUsedByOffice { get; set; }
    public bool EmailNotUsedByUser { get; set; }
    public bool NoOtherOpenRequest { get; set; }
}

public class OfficeRequestDetails
{
    public Guid Id { get; set; }
    public string ReferenceNo { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string OfficeName { get; set; } = string.Empty;
    public string CommercialRegNo { get; set; } = string.Empty;
    public string LicenseNo { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string? AdminNotes { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public string? PreviousReferenceNo { get; set; }
    public string? PreviousAdminNotes { get; set; }
    public List<RequestDocumentDto> Documents { get; set; } = new();
    public AutoChecksDto AutoChecks { get; set; } = new();
    public bool CanApprove { get; set; }
}

public class ApproveRequestBody
{
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? Note { get; set; }
}