using System.ComponentModel.DataAnnotations;

namespace Tafaqqad.Api.Dtos;

public class CreateBookingRequest
{
    [Required] public Guid? OfficeId { get; set; }

    /// The row the client picked from the office's price list (GET /api/offices/{id} → prices).
    [Required] public Guid? OfficePriceId { get; set; }

    /// "yyyy-MM-dd", e.g. "2026-11-30"
    [Required] public DateOnly? ScheduledDate { get; set; }

    [Required] public BookingPropertyInput? Property { get; set; }
}

public class BookingPropertyInput
{
    [Range(typeof(decimal), "10", "100000")] public decimal AreaM2 { get; set; }
    [Range(-5, 100)] public int? Floor { get; set; }
    [Range(0, 50)] public int? Bedrooms { get; set; }
    [Range(0, 50)] public int? Bathrooms { get; set; }
    [Range(0, 200)] public int? BuildingAge { get; set; }
    public bool HasParking { get; set; }
    public bool HasElevator { get; set; }

    [Required, StringLength(300, MinimumLength = 5)]
    public string Address { get; set; } = string.Empty;

    [Range(typeof(decimal), "-90", "90")] public decimal? Lat { get; set; }
    [Range(typeof(decimal), "-180", "180")] public decimal? Lng { get; set; }
}

public class CreateBookingResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateOnly ScheduledDate { get; set; }
    public decimal InitialPrice { get; set; }
    public string OfficeName { get; set; } = string.Empty;
    public string PropertyType { get; set; } = string.Empty;
    public string? SizeBand { get; set; }
    public bool EmailSent { get; set; }
    public string Message { get; set; } = string.Empty;
}