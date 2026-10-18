using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Models.Entities;

public class Payment
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;

    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public DateTime? PaidAt { get; set; }
    public DateTime? ReleasedAt { get; set; }    // set after the report is delivered
}