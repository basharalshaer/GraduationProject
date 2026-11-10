using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Controllers.Offices;

[Route("api/office")]
public class OfficeDashboardController : OfficeApiController
{
    private static readonly Dictionary<string, string> Labels = new()
    {
        ["ENGINEER"] = "أضف مهندساً واحداً على الأقل وفعّل حسابه",
        ["PRICE"] = "أضف سعراً واحداً على الأقل",
        ["CAPACITY"] = "حدّد السعة اليومية",
        ["HOURS"] = "حدّد أيام وساعات الدوام"
    };

    public OfficeDashboardController(AppDbContext db) : base(db) { }

    // GET api/office/dashboard
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        var office = await GetMyOfficeAsync();
        if (office is null) return NotFound(new { message = "No office is linked to this account." });

        await RefreshOfficeStatusAsync(office);   // also catches up if something changed elsewhere
        var c = await Status.GetCompletenessAsync(office);

        var engineers = await Db.Engineers.Where(e => e.OfficeId == office.Id)
            .Select(e => new { e.Status, UserStatus = e.User.Status }).ToListAsync();

        return Ok(new
        {
            office.Name,
            Status = office.Status.ToString(),
            IsLive = office.Status == OfficeStatus.Active,
            Checklist = Labels.Select(l => new { Key = l.Key, Label = l.Value, Done = !c.Missing.Contains(l.Key) }),
            Engineers = new
            {
                Active = engineers.Count(e => e.Status == EngineerStatus.Active && e.UserStatus == UserStatus.Active),
                PendingActivation = engineers.Count(e => e.Status == EngineerStatus.Active && e.UserStatus == UserStatus.PendingActivation),
                Deactivated = engineers.Count(e => e.Status == EngineerStatus.Deactivated)
            },
            c.PriceCount,
            c.DailyCapacity,
            c.OpenDays,
            office.RatingAvg,
            office.RatingCount,
            // Bookings come from Person B's part; zeros until then
            Bookings = new { NewRequests = 0, InProgress = 0, CompletedThisMonth = 0 }
        });
    }

    // GET api/office/activity
    [HttpGet("activity")]
    public async Task<IActionResult> Activity()
    {
        var office = await GetMyOfficeAsync(track: false);
        if (office is null) return NotFound(new { message = "No office is linked to this account." });

        var rows = await Db.AuditLogs.AsNoTracking()
            .Where(a => a.OfficeId == office.Id)
            .OrderByDescending(a => a.CreatedAt)
            .Take(20)
            .Select(a => new { a.Action, a.OldValue, a.NewValue, a.CreatedAt })
            .ToListAsync();

        return Ok(rows);
    }
}