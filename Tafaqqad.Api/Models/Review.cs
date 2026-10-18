namespace Tafaqqad.Api.Models.Entities;

public class Review
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;

    public Guid ClientId { get; set; }
    public User Client { get; set; } = null!;

    public Guid OfficeId { get; set; }
    public Office Office { get; set; } = null!;

    public int Rating { get; set; }            // 1 to 5
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}