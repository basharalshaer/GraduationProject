using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Dtos;

namespace Tafaqqad.Api.Controllers.Admin;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/audit-logs")]
public class AdminAuditLogsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminAuditLogsController(AppDbContext db) => _db = db;

    // GET api/admin/audit-logs?officeId=&action=&actor=&search=&from=2026-11-01&to=2026-11-10&page=1&pageSize=50
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? officeId, [FromQuery] string? action, [FromQuery] string? actor,
        [FromQuery] string? search, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        if (from is not null && to is not null && from.Value.Date > to.Value.Date)
            return BadRequest(new { message = "'from' must be on or before 'to'." });

        var q = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (officeId is not null) q = q.Where(a => a.OfficeId == officeId);
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action == action.Trim());
        if (!string.IsNullOrWhiteSpace(actor))
        {
            var term = actor.Trim();
            q = q.Where(a => _db.Users.Any(u => u.Id == a.ActorUserId && u.Email.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(a => (a.OldValue != null && a.OldValue.Contains(term))
                          || (a.NewValue != null && a.NewValue.Contains(term)));
        }
        if (from is not null)
        {
            var start = from.Value.Date;
            q = q.Where(a => a.CreatedAt >= start);
        }
        if (to is not null)
        {
            var endExclusive = to.Value.Date.AddDays(1);   // include the whole "to" day
            q = q.Where(a => a.CreatedAt < endExclusive);
        }

        var total = await q.CountAsync();

        var rows = await q
            .OrderByDescending(a => a.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                a.Id,
                a.CreatedAt,
                a.Action,
                a.OldValue,
                a.NewValue,
                a.OfficeId,
                OfficeName = _db.Offices.Where(o => o.Id == a.OfficeId).Select(o => o.Name).FirstOrDefault(),
                Actor = _db.Users.Where(u => u.Id == a.ActorUserId)
                    .Select(u => new { u.Email, u.Role }).FirstOrDefault()
            })
            .ToListAsync();

        return Ok(new PagedResult<AdminAuditLogItemDto>
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = rows.Select(r => new AdminAuditLogItemDto
            {
                Id = r.Id,
                CreatedAt = r.CreatedAt,
                Action = r.Action,
                OldValue = r.OldValue,
                NewValue = r.NewValue,
                OfficeId = r.OfficeId,
                OfficeName = r.OfficeName,
                ActorEmail = r.Actor?.Email,
                ActorRole = r.Actor?.Role.ToString()
            }).ToList()
        });
    }

    // GET api/admin/audit-logs/actions
    [HttpGet("actions")]
    public async Task<IActionResult> Actions()
    {
        var actions = await _db.AuditLogs.AsNoTracking()
            .Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
        return Ok(actions);
    }
}