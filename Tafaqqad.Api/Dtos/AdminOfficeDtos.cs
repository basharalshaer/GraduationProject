using System.ComponentModel.DataAnnotations;

namespace Tafaqqad.Api.Dtos;

public class AdminOfficeListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LicenseNo { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? OwnerEmail { get; set; }
    public int ActiveEngineers { get; set; }
    public int PriceCount { get; set; }
    public int DailyCapacity { get; set; }
    public decimal RatingAvg { get; set; }
    public int RatingCount { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

public class AdminOfficeListResponse : PagedResult<AdminOfficeListItemDto>
{
    public Dictionary<string, int> StatusCounts { get; set; } = new();   // "Active": 2, "Incomplete": 1, "Suspended": 0
}

public class AdminOfficeEngineerDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;       // engineer status
    public string UserStatus { get; set; } = string.Empty;   // PendingActivation / Active / Suspended
}

public class AdminAuditRowDto
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? ActorEmail { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AdminOfficeDetailsDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LicenseNo { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? OwnerName { get; set; }
    public string? OwnerEmail { get; set; }
    public int DailyCapacity { get; set; }
    public decimal RatingAvg { get; set; }
    public int RatingCount { get; set; }
    public OfficeCompletenessDto Completeness { get; set; } = new();
    public List<AdminOfficeEngineerDto> Engineers { get; set; } = new();
    public List<AdminAuditRowDto> RecentActivity { get; set; } = new();
}

public class SuspendOfficeRequest
{
    [Required, StringLength(500, MinimumLength = 5)]
    public string Reason { get; set; } = string.Empty;
}