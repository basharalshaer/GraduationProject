using System.ComponentModel.DataAnnotations;

namespace Tafaqqad.Api.Dtos;

public class WorkingDayDto
{
    public string Weekday { get; set; } = string.Empty;   // Sunday ... Saturday
    public bool IsClosed { get; set; }
    public string? OpenTime { get; set; }                  // "09:00"
    public string? CloseTime { get; set; }                 // "15:00"
}

public class OfficeScheduleDto
{
    public int DailyCapacity { get; set; }
    public List<WorkingDayDto> Days { get; set; } = new();
}

public class UpdateOfficeScheduleRequest
{
    [Range(1, 50)] public int DailyCapacity { get; set; }
    [Required] public List<WorkingDayDto> Days { get; set; } = new();
}