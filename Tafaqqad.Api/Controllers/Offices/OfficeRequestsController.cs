using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Dtos;
using Tafaqqad.Api.Models.Entities;
using Tafaqqad.Api.Models.Enums;
using Tafaqqad.Api.Services;

namespace Tafaqqad.Api.Controllers.Offices;

[ApiController]
[Route("api/office-requests")]
public class OfficeRequestsController : ControllerBase
{
    private const long MaxFileBytes = 5 * 1024 * 1024;   // 5 MB per file
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".pdf" };

    private readonly AppDbContext _db;
    private readonly IFileStorage _files;
    private readonly IAppEmailSender _email;

    public OfficeRequestsController(AppDbContext db, IFileStorage files, IAppEmailSender email)
    {
        _db = db;
        _files = files;
        _email = email;
    }

    // POST api/office-requests  (public: the office has no account yet)
    [HttpPost]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Submit([FromForm] SubmitOfficeRequestForm form)
    {
        var email = form.Email.Trim().ToLowerInvariant();
        var license = form.LicenseNo.Trim();

        // 1. Validate the files
        var uploads = new List<(IFormFile File, DocumentType Type, string Label)>
        {
            (form.LicenseFile, DocumentType.License, "License file"),
            (form.CommercialRegFile, DocumentType.CommercialReg, "Commercial registration file")
        };
        if (form.LogoFile is not null)
            uploads.Add((form.LogoFile, DocumentType.Logo, "Logo"));

        foreach (var (file, _, label) in uploads)
        {
            var error = await ValidateFileAsync(file);
            if (error is not null) return BadRequest(new { message = $"{label}: {error}" });
        }

        // 2. Duplicate checks
        if (await _db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { message = "An account with this email already exists." });

        if (await _db.Offices.AnyAsync(o => o.LicenseNo == license))
            return Conflict(new { message = "An office with this license number already exists." });

        if (await _db.OfficeAccountRequests.AnyAsync(r =>
                (r.Email == email || r.LicenseNo == license) && r.Status != RequestStatus.Rejected))
            return Conflict(new { message = "There is already an open request with this email or license number." });

        // 3. Re-application after a rejection (optional)
        Guid? previousId = null;
        if (!string.IsNullOrWhiteSpace(form.PreviousReferenceNo))
        {
            var previous = await _db.OfficeAccountRequests.FirstOrDefaultAsync(r =>
                r.ReferenceNo == form.PreviousReferenceNo.Trim() && r.Email == email);
            if (previous is null || previous.Status != RequestStatus.Rejected)
                return BadRequest(new { message = "The previous request was not found or was not rejected." });
            previousId = previous.Id;
        }

        // 4. Save the files, then the database rows
        var request = new OfficeAccountRequest
        {
            Id = Guid.NewGuid(),
            OfficeName = form.OfficeName.Trim(),
            CommercialRegNo = form.CommercialRegNo.Trim(),
            LicenseNo = license,
            City = form.City.Trim(),
            ContactName = form.ContactName.Trim(),
            Email = email,
            Phone = form.Phone.Trim(),
            Description = form.Description?.Trim(),
            Status = RequestStatus.Pending,
            PreviousRequestId = previousId
        };

        var folder = $"office-requests/{request.Id:N}";
        try
        {
            foreach (var (file, type, _) in uploads)
            {
                var path = await _files.SaveAsync(file, folder);
                request.Documents.Add(new RequestDocument { Id = Guid.NewGuid(), Type = type, FileUrl = path });
            }

            await SaveWithReferenceNumberAsync(request);
        }
        catch
        {
            _files.DeleteFolder(folder);      // do not leave orphan files behind
            throw;
        }

        await _email.SendAsync(email, "استلمنا طلبك في تفقّد",
            $"أهلاً {request.ContactName}،{Environment.NewLine}استلمنا طلب إنشاء حساب لـ {request.OfficeName}.{Environment.NewLine}رقم الطلب: {request.ReferenceNo}{Environment.NewLine}سنراجع الطلب ونخبرك بالنتيجة على هذا البريد.");

        return StatusCode(StatusCodes.Status201Created, new
        {
            referenceNo = request.ReferenceNo,
            message = "Request received. We will review it and email you the result."
        });
    }

    // Reference numbers look like REQ-2026-0001. The unique index protects against two requests
    // getting the same number at the same moment, so we simply try the next number.
    private async Task SaveWithReferenceNumberAsync(OfficeAccountRequest request)
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"REQ-{year}-";
        _db.OfficeAccountRequests.Add(request);

        for (var attempt = 0; ; attempt++)
        {
            var count = await _db.OfficeAccountRequests.CountAsync(r => r.ReferenceNo.StartsWith(prefix));
            request.ReferenceNo = $"{prefix}{count + 1 + attempt:D4}";
            try
            {
                await _db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                // reference number collision: loop and try the next number
            }
        }
    }

    private static async Task<string?> ValidateFileAsync(IFormFile file)
    {
        if (file.Length == 0) return "the file is empty.";
        if (file.Length > MaxFileBytes) return "the file is larger than 5 MB.";

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext)) return "only JPG, PNG or PDF files are allowed.";

        // Check the real content, not only the name: the first bytes identify the file type
        var header = new byte[4];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAsync(header.AsMemory(0, 4));
        if (read < 4) return "the file is not a valid image or PDF.";

        var isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        var isPng = header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47;
        var isPdf = header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46;

        var matches = ext switch
        {
            ".jpg" or ".jpeg" => isJpeg,
            ".png" => isPng,
            ".pdf" => isPdf,
            _ => false
        };
        return matches ? null : "the file content does not match its type.";
    }
}