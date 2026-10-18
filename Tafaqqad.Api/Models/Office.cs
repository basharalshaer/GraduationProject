using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Models.Entities;

public class Office
{
    public Guid Id { get; set; }

    public Guid OwnerUserId { get; set; }
    public User OwnerUser { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public string LicenseNo { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }

    public int DailyCapacity { get; set; }
    public OfficeStatus Status { get; set; } = OfficeStatus.Incomplete;

    // Stored copies, updated whenever a review is added
    public decimal RatingAvg { get; set; }
    public int RatingCount { get; set; }

    public ICollection<OfficeWorkingHour> WorkingHours { get; set; } = new List<OfficeWorkingHour>();
    public ICollection<OfficePrice> Prices { get; set; } = new List<OfficePrice>();
    public ICollection<Engineer> Engineers { get; set; } = new List<Engineer>();
}