using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Dtos;
using Tafaqqad.Api.Models.Enums;
using Tafaqqad.Api.Services;

namespace Tafaqqad.Api.Controllers.Admin;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/office-requests")]
public class AdminOfficeRequestsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _files;

    public AdminOfficeRequestsController(AppDbContext db, IFileStorage files)
    {
        _db = db;
        _files = files;
    }

    // GET api/admin/office-requests?status=Pending&search=...&page=1&pageSize=20
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? search,
                                          [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var q = _db.OfficeAccountRequests.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<RequestStatus>(status, ignoreCase: true, out var st))
                return BadRequest(new { message = "Unknown status. Use Pending, InfoRequested, Approved or Rejected." });
            q = q.Where(r => r.Status == st);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(r => r.ReferenceNo.Contains(s) || r.OfficeName.Contains(s)
                          || r.Email.Contains(s) || r.LicenseNo.Contains(s));
        }

        var total = await q.CountAsync();

        var items = await q
            .OrderBy(r => r.Status == RequestStatus.Pending ? 0 : r.Status == RequestStatus.InfoRequested ? 1 : 2)
            .ThenBy(r => r.CreatedAt)                       // oldest waiting request first
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new OfficeRequestListItem
            {
                Id = r.Id,
                ReferenceNo = r.ReferenceNo,
                OfficeName = r.OfficeName,
                City = r.City,
                LicenseNo = r.LicenseNo,
                Email = r.Email,
                Status = r.Status.ToString(),
                SubmittedAt = r.CreatedAt,
                IsReapplication = r.PreviousRequestId != null
            })
            .ToListAsync();

        var raw = await _db.OfficeAccountRequests.AsNoTracking()
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();
        var counts = Enum.GetValues<RequestStatus>().ToDictionary(
            s => s.ToString(), s => raw.FirstOrDefault(x => x.Status == s)?.Count ?? 0);

        return Ok(new OfficeRequestListResponse
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Counts = counts,
            Items = items
        });
    }

    // GET api/admin/office-requests/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id)
    {
        var r = await _db.OfficeAccountRequests.AsNoTracking()
            .Include(x => x.Documents)
            .Include(x => x.ReviewedByUser)
            .Include(x => x.PreviousRequest)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (r is null) return NotFound(new { message = "Request not found." });

        var checks = new AutoChecksDto
        {
            LicenseNotUsedByOffice = !await _db.Offices.AnyAsync(o => o.LicenseNo == r.LicenseNo),
            EmailNotUsedByUser = !await _db.Users.AnyAsync(u => u.Email == r.Email),
            NoOtherOpenRequest = !await _db.OfficeAccountRequests.AnyAsync(x =>
                x.Id != r.Id && (x.Email == r.Email || x.LicenseNo == r.LicenseNo)
                && x.Status != RequestStatus.Rejected)
        };

        return Ok(new OfficeRequestDetails
        {
            Id = r.Id,
            ReferenceNo = r.ReferenceNo,
            Status = r.Status.ToString(),
            OfficeName = r.OfficeName,
            CommercialRegNo = r.CommercialRegNo,
            LicenseNo = r.LicenseNo,
            City = r.City,
            ContactName = r.ContactName,
            Email = r.Email,
            Phone = r.Phone,
            Description = r.Description,
            SubmittedAt = r.CreatedAt,
            AdminNotes = r.AdminNotes,
            ReviewedAt = r.ReviewedAt,
            ReviewedBy = r.ReviewedByUser?.FullName,
            PreviousReferenceNo = r.PreviousRequest?.ReferenceNo,
            PreviousAdminNotes = r.PreviousRequest?.AdminNotes,
            Documents = r.Documents.Select(d => new RequestDocumentDto
            {
                Id = d.Id,
                Type = d.Type.ToString(),
                FileName = Path.GetFileName(d.FileUrl)
            }).ToList(),
            AutoChecks = checks,
            CanApprove = r.Status == RequestStatus.Pending
                         && checks.LicenseNotUsedByOffice && checks.EmailNotUsedByUser && checks.NoOtherOpenRequest
        });
    }

    // GET api/admin/office-requests/{id}/documents/{docId}  (admin only: files are not public)
    [HttpGet("{id:guid}/documents/{docId:guid}")]
    public async Task<IActionResult> Document(Guid id, Guid docId)
    {
        var doc = await _db.RequestDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == docId && d.RequestId == id);
        if (doc is null) return NotFound(new { message = "Document not found." });

        var path = _files.ResolvePath(doc.FileUrl);
        if (path is null) return NotFound(new { message = "The file is missing on the server." });

        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, contentType);       // shown in the browser, not forced as a download
    }
}