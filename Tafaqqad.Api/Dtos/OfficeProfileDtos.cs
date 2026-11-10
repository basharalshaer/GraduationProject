using System.ComponentModel.DataAnnotations;

namespace Tafaqqad.Api.Dtos;

public class OfficeCompletenessDto
{
    public int ActiveEngineers { get; set; }
    public int PriceCount { get; set; }
    public int DailyCapacity { get; set; }
    public bool IsComplete { get; set; }
    public List<string> Missing { get; set; } = new();   // ENGINEER, PRICE, CAPACITY
}

public class OfficeProfileDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LicenseNo { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public int DailyCapacity { get; set; }
    public decimal RatingAvg { get; set; }
    public int RatingCount { get; set; }
    public OfficeCompletenessDto Completeness { get; set; } = new();
}

public class UpdateOfficeProfileRequest
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string City { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Phone { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Description { get; set; }
}