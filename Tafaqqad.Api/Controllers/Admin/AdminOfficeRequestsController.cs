using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Dtos;
using Tafaqqad.Api.Models.Enums;
using Tafaqqad.Api.Services;
using Tafaqqad.Api.Models.Entities;

namespace Tafaqqad.Api.Controllers.Admin;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/office-requests")]
public class AdminOfficeRequestsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _files;
    private readonly IAuthTokenService _tokens;
    private readonly IAppEmailSender _email;
    private readonly IConfiguration _config;
    private readonly ILogger<AdminOfficeRequestsController> _log;

    public AdminOfficeRequestsController(AppDbContext db, IFileStorage files, IAuthTokenService tokens,
        IAppEmailSender email, IConfiguration config, ILogger<AdminOfficeRequestsController> log)
    {
        _db = db;
        _files = files;
        _tokens = tokens;
        _email = email;
        _config = config;
        _log = log;
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;

    private string ActivationLink(string token) =>
        $"{_config["App:FrontendBaseUrl"]!.TrimEnd('/')}/set-password?token={Uri.EscapeDataString(token)}";

    private async Task<bool> TrySendActivationAsync(User user, string officeName, string rawToken)
    {
        try
        {
            await _email.SendAsync(user.Email, "تم قبول طلبك في تفقّد",
                $"أهلاً {user.FullName}،{Environment.NewLine}تمت الموافقة على طلب {officeName}.{Environment.NewLine}عيّن كلمة المرور من الرابط التالي (صالح 48 ساعة):{Environment.NewLine}{ActivationLink(rawToken)}{Environment.NewLine}بعد الدخول، أكمل ملف مكتبك بإضافة المهندسين وقائمة الأسعار.");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not send the activation email to {Email}", user.Email);
            return false;
        }
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

    // POST api/admin/office-requests/{id}/approve
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, ApproveRequestBody body)
    {
        var adminId = CurrentUserId();
        if (adminId is null) return Unauthorized();

        var r = await _db.OfficeAccountRequests.Include(x => x.Documents).FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return NotFound(new { message = "Request not found." });

        if (r.Status != RequestStatus.Pending)
            return Conflict(new { message = $"Only pending requests can be approved. This one is {r.Status}." });

        // Re-check at approval time: things may have changed since the request was submitted
        if (await _db.Users.AnyAsync(u => u.Email == r.Email))
            return Conflict(new { message = "An account with this email already exists." });
        if (await _db.Offices.AnyAsync(o => o.LicenseNo == r.LicenseNo))
            return Conflict(new { message = "An office with this license number already exists." });
        if (await _db.OfficeAccountRequests.AnyAsync(x => x.Id != r.Id
                && (x.Email == r.Email || x.LicenseNo == r.LicenseNo) && x.Status != RequestStatus.Rejected))
            return Conflict(new { message = "Another open request uses this email or license number." });

        var user = new User
        {
            Id = Guid.NewGuid(),
            Role = UserRole.Office,
            Email = r.Email,
            FullName = r.ContactName,
            Phone = r.Phone,
            Status = UserStatus.PendingActivation,    // active after the office sets its password
            PasswordHash = null
        };

        var office = new Office
        {
            Id = Guid.NewGuid(),
            OwnerUserId = user.Id,
            Name = r.OfficeName,
            LicenseNo = r.LicenseNo,
            City = r.City,
            Phone = r.Phone,
            Description = r.Description,
            LogoUrl = r.Documents.FirstOrDefault(d => d.Type == DocumentType.Logo)?.FileUrl,
            DailyCapacity = 0,                        // the office sets this itself
            Status = OfficeStatus.Incomplete          // listed publicly only after it has engineers and prices
        };

        _db.Users.Add(user);
        _db.Offices.Add(office);

        var rawToken = await _tokens.CreateAsync(user, TokenType.SetPassword, TimeSpan.FromHours(48));

        r.Status = RequestStatus.Approved;
        r.ReviewedByUserId = adminId;
        r.ReviewedAt = DateTime.UtcNow;
        r.AdminNotes = string.IsNullOrWhiteSpace(body.Note) ? null : body.Note.Trim();

        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = adminId.Value,
            OfficeId = office.Id,
            Action = "OfficeApproved",
            NewValue = r.ReferenceNo
        });

        try
        {
            await _db.SaveChangesAsync();             // everything above is saved together, or not at all
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Could not approve: a duplicate email or license was created in the meantime." });
        }

        var emailSent = await TrySendActivationAsync(user, office.Name, rawToken);

        return Ok(new
        {
            officeId = office.Id,
            userId = user.Id,
            emailSent,
            message = emailSent
                ? "Approved. An activation link was emailed to the office."
                : "Approved, but the email could not be sent. Use resend-activation."
        });
    }

    // POST api/admin/office-requests/{id}/resend-activation
    [HttpPost("{id:guid}/resend-activation")]
    public async Task<IActionResult> ResendActivation(Guid id)
    {
        var r = await _db.OfficeAccountRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return NotFound(new { message = "Request not found." });
        if (r.Status != RequestStatus.Approved)
            return Conflict(new { message = "Only approved requests have an activation link." });

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == r.Email && u.Role == UserRole.Office);
        if (user is null) return NotFound(new { message = "The office account was not found." });
        if (user.Status != UserStatus.PendingActivation)
            return Conflict(new { message = "This account is already activated." });

        var rawToken = await _tokens.CreateAsync(user, TokenType.SetPassword, TimeSpan.FromHours(48));
        await _db.SaveChangesAsync();

        var emailSent = await TrySendActivationAsync(user, r.OfficeName, rawToken);
        return Ok(new { emailSent, message = emailSent ? "A new activation link was sent." : "The email could not be sent." });
    }

    private async Task<bool> TrySendAsync(string to, string subject, string body)
    {
        try { await _email.SendAsync(to, subject, body); return true; }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not send email to {Email}", to);
            return false;
        }
    }

    // POST api/admin/office-requests/{id}/reject
    [HttpPost("{id:guid}/reject")]
    public async Task<IActionResult> Reject(Guid id, ReasonBody body)
    {
        var adminId = CurrentUserId();
        if (adminId is null) return Unauthorized();

        var r = await _db.OfficeAccountRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return NotFound(new { message = "Request not found." });

        if (r.Status != RequestStatus.Pending && r.Status != RequestStatus.InfoRequested)
            return Conflict(new { message = $"Only pending or info-requested requests can be rejected. This one is {r.Status}." });

        var reason = body.Reason.Trim();
        r.Status = RequestStatus.Rejected;
        r.ReviewedByUserId = adminId;
        r.ReviewedAt = DateTime.UtcNow;
        r.AdminNotes = reason;

        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = adminId.Value,
            Action = "OfficeRequestRejected",
            NewValue = $"{r.ReferenceNo}: {reason}"
        });
        await _db.SaveChangesAsync();

        var emailSent = await TrySendAsync(r.Email, "نتيجة طلبك في تفقّد",
            $"أهلاً {r.ContactName}،{Environment.NewLine}نأسف، لم نتمكن من قبول طلب {r.OfficeName} (رقم الطلب {r.ReferenceNo}).{Environment.NewLine}السبب: {reason}{Environment.NewLine}يمكنك تقديم طلب جديد بعد معالجة السبب، مع إدخال رقم هذا الطلب ({r.ReferenceNo}) كطلب سابق.");

        return Ok(new { emailSent, message = emailSent ? "Rejected. The applicant was emailed." : "Rejected, but the email could not be sent." });
    }

    // POST api/admin/office-requests/{id}/request-info
    [HttpPost("{id:guid}/request-info")]
    public async Task<IActionResult> RequestInfo(Guid id, InfoRequestBody body)
    {
        var adminId = CurrentUserId();
        if (adminId is null) return Unauthorized();

        var r = await _db.OfficeAccountRequests.FirstOrDefaultAsync(x => x.Id == id);
        if (r is null) return NotFound(new { message = "Request not found." });

        if (r.Status != RequestStatus.Pending)
            return Conflict(new { message = $"Only pending requests can be sent back for information. This one is {r.Status}." });

        var message = body.Message.Trim();
        r.Status = RequestStatus.InfoRequested;
        r.ReviewedByUserId = adminId;
        r.ReviewedAt = DateTime.UtcNow;
        r.AdminNotes = message;

        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            ActorUserId = adminId.Value,
            Action = "OfficeRequestInfoRequested",
            NewValue = $"{r.ReferenceNo}: {message}"
        });
        await _db.SaveChangesAsync();

        var emailSent = await TrySendAsync(r.Email, "نحتاج معلومات إضافية لطلبك في تفقّد",
            $"أهلاً {r.ContactName}،{Environment.NewLine}نحتاج المعلومات التالية لإكمال مراجعة طلب {r.OfficeName} (رقم الطلب {r.ReferenceNo}):{Environment.NewLine}{message}{Environment.NewLine}ادخل رقم الطلب والبريد في صفحة \"تتبّع طلبي\" لتحديث المستندات أو الوصف.");

        return Ok(new { emailSent, message = emailSent ? "Information requested. The applicant was emailed." : "Status updated, but the email could not be sent." });
    }
}