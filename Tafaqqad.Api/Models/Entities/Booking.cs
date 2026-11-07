using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Models.Entities;

public class Booking
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }
    public User Client { get; set; } = null!;

    public Guid OfficeId { get; set; }
    public Office Office { get; set; } = null!;

    public Guid PropertyId { get; set; }
    public Property Property { get; set; } = null!;

    public Guid? EngineerId { get; set; }       // empty until the office assigns someone
    public Engineer? Engineer { get; set; }

    public DateOnly ScheduledDate { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Requested;

    public decimal InitialPrice { get; set; }
    public decimal? FinalPrice { get; set; }
    public string? PriceReason { get; set; }
    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<BookingStatusHistory> StatusHistory { get; set; } = new List<BookingStatusHistory>();
    public Payment? Payment { get; set; }
    public InspectionReport? Report { get; set; }
    public Review? Review { get; set; }
}