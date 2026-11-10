using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Models.Entities;
using Tafaqqad.Api.Services;

namespace Tafaqqad.Api.Controllers.Offices;

[ApiController]
[Authorize(Roles = "Office")]
public abstract class OfficeApiController : ControllerBase
{
    protected readonly AppDbContext Db;

    protected OfficeApiController(AppDbContext db) => Db = db;

    protected Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out var id) ? id : null;

    /// The office owned by the logged-in user (never trusts an id sent by the client).
    protected async Task<Office?> GetMyOfficeAsync(bool track = true)
    {
        var userId = CurrentUserId;
        if (userId is null) return null;

        var q = track ? Db.Offices.AsQueryable() : Db.Offices.AsNoTracking();
        return await q.FirstOrDefaultAsync(o => o.OwnerUserId == userId);
    }

    protected OfficeStatusService Status =>
    HttpContext.RequestServices.GetRequiredService<OfficeStatusService>();

    /// Call after SaveChangesAsync whenever engineers, prices, capacity or hours change.
    protected Task RefreshOfficeStatusAsync(Office office) =>
        Status.RefreshAsync(office, CurrentUserId!.Value);
}