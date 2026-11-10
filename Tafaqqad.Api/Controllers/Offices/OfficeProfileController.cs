using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Dtos;
using Tafaqqad.Api.Models.Entities;
using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Controllers.Offices;

[Route("api/office/profile")]
public class OfficeProfileController : OfficeApiController
{
    public OfficeProfileController(AppDbContext db) : base(db) { }

    // GET api/office/profile
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var office = await GetMyOfficeAsync(track: false);
        if (office is null) return NotFound(new { message = "No office is linked to this account." });

        return Ok(await BuildProfileAsync(office));
    }

    // PUT api/office/profile
    [HttpPut]
    public async Task<IActionResult> Update(UpdateOfficeProfileRequest req)
    {
        var office = await GetMyOfficeAsync();
        if (office is null) return NotFound(new { message = "No office is linked to this account." });

        if (office.Status == OfficeStatus.Suspended)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "This office is suspended. Please contact support." });

        var oldName = office.Name;
        var newName = req.Name.Trim();

        office.Name = newName;
        office.City = req.City.Trim();
        office.Phone = req.Phone.Trim();
        office.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();

        if (!string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            Db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorUserId = CurrentUserId!.Value,
                OfficeId = office.Id,
                Action = "OfficeNameChanged",
                OldValue = oldName,
                NewValue = newName,
                CreatedAt = DateTime.UtcNow
            });
        }

        await Db.SaveChangesAsync();
        return Ok(await BuildProfileAsync(office));
    }

    private async Task<OfficeProfileDto> BuildProfileAsync(Office office) => new()
    {
        Id = office.Id,
        Name = office.Name,
        LicenseNo = office.LicenseNo,
        City = office.City,
        Phone = office.Phone,
        Description = office.Description,
        Status = office.Status.ToString(),
        DailyCapacity = office.DailyCapacity,
        RatingAvg = office.RatingAvg,
        RatingCount = office.RatingCount,
        Completeness = await Status.GetCompletenessAsync(office)
    };
}