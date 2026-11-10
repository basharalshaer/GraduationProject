using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Dtos;
using Tafaqqad.Api.Models.Entities;
using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Controllers.Offices;

[Route("api/office/schedule")]
public class OfficeScheduleController : OfficeApiController
{
    // Jordan's week starts on Sunday
    private static readonly DayOfWeek[] WeekOrder =
        { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday };

    public OfficeScheduleController(AppDbContext db) : base(db) { }

    // GET api/office/schedule
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var office = await GetMyOfficeAsync(track: false);
        if (office is null) return NotFound(new { message = "No office is linked to this account." });

        return Ok(await LoadAsync(office));
    }

    // PUT api/office/schedule  (replaces capacity + all 7 days)
    [HttpPut]
    public async Task<IActionResult> Replace(UpdateOfficeScheduleRequest req)
    {
        var office = await GetMyOfficeAsync();
        if (office is null) return NotFound(new { message = "No office is linked to this account." });
        if (office.Status == OfficeStatus.Suspended)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This office is suspended. Please contact support." });

        // 1) Validate: exactly the 7 days, each once, with valid times
        if (req.Days.Count != 7)
            return BadRequest(new { message = "Send all 7 days of the week." });

        var parsed = new Dictionary<DayOfWeek, (bool Closed, TimeOnly? Open, TimeOnly? Close)>();
        foreach (var d in req.Days)
        {
            if (!Enum.TryParse<DayOfWeek>(d.Weekday, ignoreCase: true, out var day) || !Enum.IsDefined(day))
                return BadRequest(new { message = $"Unknown weekday '{d.Weekday}'." });
            if (parsed.ContainsKey(day))
                return BadRequest(new { message = $"{day} appears more than once." });

            if (d.IsClosed) { parsed[day] = (true, null, null); continue; }

            if (!TryTime(d.OpenTime, out var open) || !TryTime(d.CloseTime, out var close))
                return BadRequest(new { message = $"{day}: use the HH:mm format, for example 09:00." });
            if (open >= close)
                return BadRequest(new { message = $"{day}: the closing time must be after the opening time." });

            parsed[day] = (false, open, close);
        }
        if (parsed.Values.All(v => v.Closed))
            return BadRequest(new { message = "The office must be open at least one day a week." });

        // 2) Save: capacity + upsert the 7 rows, in one SaveChanges
        if (office.DailyCapacity != req.DailyCapacity)
        {
            Db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = CurrentUserId!.Value,
                OfficeId = office.Id,
                Action = "CapacityChanged",
                OldValue = office.DailyCapacity.ToString(),
                NewValue = req.DailyCapacity.ToString(),
                CreatedAt = DateTime.UtcNow
            });
            office.DailyCapacity = req.DailyCapacity;
        }

        var rows = await Db.OfficeWorkingHours.Where(w => w.OfficeId == office.Id).ToListAsync();
        foreach (var (day, v) in parsed)
        {
            var row = rows.FirstOrDefault(r => r.Weekday == day);
            if (row is null)
            {
                row = new OfficeWorkingHour { Id = Guid.NewGuid(), OfficeId = office.Id, Weekday = day };
                Db.OfficeWorkingHours.Add(row);
            }
            row.IsClosed = v.Closed;
            row.OpenTime = v.Open;
            row.CloseTime = v.Close;
        }

        await Db.SaveChangesAsync();
        return Ok(await LoadAsync(office));
    }

    private async Task<OfficeScheduleDto> LoadAsync(Office office)
    {
        var rows = await Db.OfficeWorkingHours.AsNoTracking().Where(w => w.OfficeId == office.Id).ToListAsync();
        return new OfficeScheduleDto
        {
            DailyCapacity = office.DailyCapacity,
            Days = WeekOrder.Select(day =>
            {
                var r = rows.FirstOrDefault(x => x.Weekday == day);
                // A day that was never set counts as closed
                return new WorkingDayDto
                {
                    Weekday = day.ToString(),
                    IsClosed = r is null || r.IsClosed,
                    OpenTime = r?.OpenTime?.ToString("HH:mm"),
                    CloseTime = r?.CloseTime?.ToString("HH:mm")
                };
            }).ToList()
        };
    }

    private static bool TryTime(string? s, out TimeOnly t) =>
        TimeOnly.TryParseExact(s ?? string.Empty, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out t);
}