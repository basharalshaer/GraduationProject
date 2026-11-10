using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Dtos;
using Tafaqqad.Api.Models.Entities;
using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Services;

public class OfficeStatusService
{
    private readonly AppDbContext _db;
    public OfficeStatusService(AppDbContext db) => _db = db;

    public async Task<OfficeCompletenessDto> GetCompletenessAsync(Office office)
    {
        // Only engineers who are active AND have set their password can actually work
        var activeEngineers = await _db.Engineers.CountAsync(e => e.OfficeId == office.Id
            && e.Status == EngineerStatus.Active && e.User.Status == UserStatus.Active);
        var priceCount = await _db.OfficePrices.CountAsync(p => p.OfficeId == office.Id);
        var openDays = await _db.OfficeWorkingHours.CountAsync(w => w.OfficeId == office.Id && !w.IsClosed);

        var missing = new List<string>();
        if (activeEngineers == 0) missing.Add("ENGINEER");
        if (priceCount == 0) missing.Add("PRICE");
        if (office.DailyCapacity <= 0) missing.Add("CAPACITY");
        if (openDays == 0) missing.Add("HOURS");

        return new OfficeCompletenessDto
        {
            ActiveEngineers = activeEngineers,
            PriceCount = priceCount,
            DailyCapacity = office.DailyCapacity,
            OpenDays = openDays,
            IsComplete = missing.Count == 0,
            Missing = missing
        };
    }

    /// Moves the office between Incomplete and Active to match the rule. Never touches Suspended.
    /// The office must be tracked. Saves only if the status changes.
    public async Task RefreshAsync(Office office, Guid actorUserId)
    {
        if (office.Status == OfficeStatus.Suspended) return;

        var c = await GetCompletenessAsync(office);
        var target = c.IsComplete ? OfficeStatus.Active : OfficeStatus.Incomplete;
        if (office.Status == target) return;

        var old = office.Status;
        office.Status = target;
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            OfficeId = office.Id,
            Action = target == OfficeStatus.Active ? "OfficeActivated" : "OfficeBackToIncomplete",
            OldValue = old.ToString(),
            NewValue = target.ToString(),
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}