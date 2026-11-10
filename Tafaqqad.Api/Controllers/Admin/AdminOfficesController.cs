using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Dtos;
using Tafaqqad.Api.Models.Entities;
using Tafaqqad.Api.Models.Enums;
using Tafaqqad.Api.Services;

namespace Tafaqqad.Api.Controllers.Admin;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/offices")]
public class AdminOfficesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly OfficeStatusService _status;
    private readonly IAppEmailSender _email;
    private readonly ILogger<AdminOfficesController> _logger;

    public AdminOfficesController(AppDbContext db, OfficeStatusService status,
        IAppEmailSender email, ILogger<AdminOfficesController> logger)
    {
        _db = db;
        _status = status;
        _email = email;
        _logger = logger;
    }

    private Guid AdminId =>
        Guid.Parse(User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // GET api/admin/offices?status=Active&search=&page=1&pageSize=20
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = _db.Offices.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<OfficeStatus>(status, ignoreCase: true, out var s) || !Enum.IsDefined(s))
                return BadRequest(new { message = $"Unknown status '{status}'." });
            q = q.Where(o => o.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(o => o.Name.Contains(term) || o.LicenseNo.Contains(term) || o.City.Contains(term));
        }

        var total = await q.CountAsync();

        var rows = await q
            .OrderBy(o => o.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new
            {
                o.Id,
                o.Name,
                o.LicenseNo,
                o.City,
                o.Status,
                o.DailyCapacity,
                o.RatingAvg,
                o.RatingCount,
                OwnerEmail = _db.Users.Where(u => u.Id == o.OwnerUserId).Select(u => u.Email).FirstOrDefault(),
                ActiveEngineers = _db.Engineers.Count(e => e.OfficeId == o.Id
                    && e.Status == EngineerStatus.Active && e.User.Status == UserStatus.Active),
                PriceCount = _db.OfficePrices.Count(p => p.OfficeId == o.Id),
                ApprovedAt = _db.AuditLogs.Where(a => a.OfficeId == o.Id && a.Action == "OfficeApproved")
                    .Max(a => (DateTime?)a.CreatedAt)
            })
            .ToListAsync();

        // Counts per status for the tabs (ignores the filters, so the tabs always show the full picture)
        var counts = await _db.Offices.AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();

        return Ok(new AdminOfficeListResponse
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            StatusCounts = Enum.GetValues<OfficeStatus>()
                .ToDictionary(s => s.ToString(), s => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0),
            Items = rows.Select(r => new AdminOfficeListItemDto
            {
                Id = r.Id,
                Name = r.Name,
                LicenseNo = r.LicenseNo,
                City = r.City,
                Status = r.Status.ToString(),
                OwnerEmail = r.OwnerEmail,
                ActiveEngineers = r.ActiveEngineers,
                PriceCount = r.PriceCount,
                DailyCapacity = r.DailyCapacity,
                RatingAvg = r.RatingAvg,
                RatingCount = r.RatingCount,
                ApprovedAt = r.ApprovedAt
            }).ToList()
        });
    }

    // GET api/admin/offices/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id)
    {
        var office = await _db.Offices.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        if (office is null) return NotFound(new { message = "Office not found." });

        var owner = await _db.Users.AsNoTracking()
            .Where(u => u.Id == office.OwnerUserId)
            .Select(u => new { u.FullName, u.Email })
            .FirstOrDefaultAsync();

        var engineers = await _db.Engineers.AsNoTracking()
            .Where(e => e.OfficeId == office.Id)
            .OrderBy(e => e.User.FullName)
            .Select(e => new { e.Id, e.User.FullName, e.User.Email, e.Status, UserStatus = e.User.Status })
            .ToListAsync();

        var activity = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.OfficeId == office.Id)
            .OrderByDescending(a => a.CreatedAt)
            .Take(20)
            .Select(a => new
            {
                a.Id,
                a.Action,
                a.OldValue,
                a.NewValue,
                a.CreatedAt,
                ActorEmail = _db.Users.Where(u => u.Id == a.ActorUserId).Select(u => u.Email).FirstOrDefault()
            })
            .ToListAsync();

        return Ok(new AdminOfficeDetailsDto
        {
            Id = office.Id,
            Name = office.Name,
            LicenseNo = office.LicenseNo,
            City = office.City,
            Phone = office.Phone,
            Description = office.Description,
            Status = office.Status.ToString(),
            OwnerName = owner?.FullName,
            OwnerEmail = owner?.Email,
            DailyCapacity = office.DailyCapacity,
            RatingAvg = office.RatingAvg,
            RatingCount = office.RatingCount,
            Completeness = await _status.GetCompletenessAsync(office),
            Engineers = engineers.Select(e => new AdminOfficeEngineerDto
            {
                Id = e.Id,
                FullName = e.FullName,
                Email = e.Email,
                Status = e.Status.ToString(),
                UserStatus = e.UserStatus.ToString()
            }).ToList(),
            RecentActivity = activity.Select(a => new AdminAuditRowDto
            {
                Id = a.Id,
                Action = a.Action,
                OldValue = a.OldValue,
                NewValue = a.NewValue,
                ActorEmail = a.ActorEmail,
                CreatedAt = a.CreatedAt
            }).ToList()
        });
    }

    // POST api/admin/offices/{id}/suspend   { "reason": "..." }
    [HttpPost("{id:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid id, SuspendOfficeRequest request)
    {
        var office = await _db.Offices.FirstOrDefaultAsync(o => o.Id == id);
        if (office is null) return NotFound(new { message = "Office not found." });
        if (office.Status == OfficeStatus.Suspended)
            return Conflict(new { message = "This office is already suspended." });

        var reason = request.Reason.Trim();
        var oldStatus = office.Status;
        office.Status = OfficeStatus.Suspended;

        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = AdminId,
            OfficeId = office.Id,
            Action = "OfficeSuspended",
            OldValue = oldStatus.ToString(),
            NewValue = reason,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        var emailSent = await NotifyOwnerAsync(office,
            "تم تعليق حساب مكتبكم على منصة تفقّد",
            $"مرحباً،\n\nتم تعليق مكتب \"{office.Name}\" على منصة تفقّد، ولن يظهر للعملاء حتى يتم رفع التعليق.\n\nالسبب: {reason}\n\nيمكنكم تسجيل الدخول للاطلاع على بياناتكم، وللاستفسار يرجى التواصل مع إدارة المنصة.\n\nفريق تفقّد");

        return Ok(new { status = office.Status.ToString(), emailSent });
    }

    // POST api/admin/offices/{id}/reactivate
    [HttpPost("{id:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        var office = await _db.Offices.FirstOrDefaultAsync(o => o.Id == id);
        if (office is null) return NotFound(new { message = "Office not found." });
        if (office.Status != OfficeStatus.Suspended)
            return Conflict(new { message = "This office is not suspended." });

        // Back to Incomplete first; the 6f rule then decides if it can go live
        office.Status = OfficeStatus.Incomplete;

        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = AdminId,
            OfficeId = office.Id,
            Action = "OfficeReactivated",
            OldValue = OfficeStatus.Suspended.ToString(),
            NewValue = OfficeStatus.Incomplete.ToString(),
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await _status.RefreshAsync(office, AdminId);   // Incomplete → Active if engineers, prices, capacity and hours are there

        var live = office.Status == OfficeStatus.Active;
        var emailSent = await NotifyOwnerAsync(office,
            "تم رفع التعليق عن مكتبكم على منصة تفقّد",
            live
                ? $"مرحباً،\n\nتم رفع التعليق عن مكتب \"{office.Name}\"، وأصبح ظاهراً للعملاء من جديد.\n\nفريق تفقّد"
                : $"مرحباً،\n\nتم رفع التعليق عن مكتب \"{office.Name}\". لن يظهر المكتب للعملاء حتى تكتمل بياناته (المهندسون، الأسعار، السعة اليومية وساعات العمل). يمكنكم إكمالها من لوحة المكتب.\n\nفريق تفقّد");

        return Ok(new { status = office.Status.ToString(), emailSent });
    }

    private async Task<bool> NotifyOwnerAsync(Office office, string subject, string body)
    {
        var email = await _db.Users.Where(u => u.Id == office.OwnerUserId).Select(u => u.Email).FirstOrDefaultAsync();
        if (string.IsNullOrEmpty(email)) return false;
        try
        {
            await _email.SendAsync(email, subject, body);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to email office owner {OfficeId}", office.Id);
            return false;
        }
    }
}