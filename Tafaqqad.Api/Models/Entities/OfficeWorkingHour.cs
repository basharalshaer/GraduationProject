namespace Tafaqqad.Api.Models.Entities;

public class OfficeWorkingHour
{
    public Guid Id { get; set; }
    public Guid OfficeId { get; set; }
    public Office Office { get; set; } = null!;

    public DayOfWeek Weekday { get; set; }
    public TimeOnly? OpenTime { get; set; }    // null when the office is closed that day
    public TimeOnly? CloseTime { get; set; }
    public bool IsClosed { get; set; }
}