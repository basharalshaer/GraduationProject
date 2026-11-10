using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Dtos;
using Tafaqqad.Api.Models.Entities;
using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Controllers.Offices;

[Route("api/office/prices")]
public class OfficePricesController : OfficeApiController
{
    public OfficePricesController(AppDbContext db) : base(db) { }

    // GET api/office/prices
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var office = await GetMyOfficeAsync(track: false);
        if (office is null) return NotFound(new { message = "No office is linked to this account." });

        return Ok(await LoadAsync(office.Id));
    }

    // PUT api/office/prices  (replaces the whole list)
    [HttpPut]
    public async Task<IActionResult> Replace(UpdateOfficePricesRequest req)
    {
        var office = await GetMyOfficeAsync();
        if (office is null) return NotFound(new { message = "No office is linked to this account." });
        if (office.Status == OfficeStatus.Suspended)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "This office is suspended. Please contact support." });

        // 1) Validate and normalise the incoming rows
        var incoming = new List<(PropertyType Type, string? Band, decimal Price)>();
        var seen = new HashSet<string>();
        foreach (var item in req.Items)
        {
            if (!Enum.TryParse<PropertyType>(item.PropertyType, ignoreCase: true, out var type) || !Enum.IsDefined(type))
                return BadRequest(new { message = $"Unknown property type '{item.PropertyType}'." });

            var band = string.IsNullOrWhiteSpace(item.SizeBand) ? string.Empty : item.SizeBand.Trim();
            var key = Key(type, band);
            if (!seen.Add(key))
                return BadRequest(new { message = $"Duplicate price row: {type} {band}." });

            incoming.Add((type, band, Math.Round(item.Price, 2)));
        }

        // 2) Compare with what is stored: update, add, remove
        var existing = await Db.OfficePrices.Where(p => p.OfficeId == office.Id).ToListAsync();
        var actor = CurrentUserId!.Value;

        foreach (var (type, band, price) in incoming)
        {
            var row = existing.FirstOrDefault(p => Key(p.PropertyType, p.SizeBand) == Key(type, band));
            if (row is null)
            {
                Db.OfficePrices.Add(new OfficePrice { Id = Guid.NewGuid(), OfficeId = office.Id, PropertyType = type, SizeBand = band, Price = price });
                Log(actor, office.Id, "PriceAdded", null, Describe(type, band, price));
            }
            else if (row.Price != price)
            {
                Log(actor, office.Id, "PriceChanged", Describe(type, band, row.Price), Describe(type, band, price));
                row.Price = price;
            }
        }

        foreach (var row in existing.Where(p => !incoming.Any(i => Key(i.Type, i.Band) == Key(p.PropertyType, p.SizeBand))))
        {
            Db.OfficePrices.Remove(row);
            Log(actor, office.Id, "PriceRemoved", Describe(row.PropertyType, row.SizeBand, row.Price), null);
        }

        await Db.SaveChangesAsync(); 
        await RefreshOfficeStatusAsync(office);
        return Ok(await LoadAsync(office.Id));
    }

    private async Task<List<OfficePriceDto>> LoadAsync(Guid officeId) =>
        (await Db.OfficePrices.AsNoTracking().Where(p => p.OfficeId == officeId).ToListAsync())
            .OrderBy(p => p.PropertyType).ThenBy(p => p.Price)
            .Select(p => new OfficePriceDto { Id = p.Id, PropertyType = p.PropertyType.ToString(), SizeBand = string.IsNullOrEmpty(p.SizeBand) ? null : p.SizeBand, Price = p.Price })
            .ToList();

    private static string Key(PropertyType type, string? band) => $"{type}|{(band ?? string.Empty).Trim().ToLowerInvariant()}";

    private static string Describe(PropertyType type, string? band, decimal price) =>
        string.IsNullOrEmpty(band) ? $"{type}: {price:0.##} JOD" : $"{type} ({band}): {price:0.##} JOD";

    private void Log(Guid actor, Guid officeId, string action, string? oldValue, string? newValue) =>
        Db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ActorUserId = actor, OfficeId = officeId, Action = action, OldValue = oldValue, NewValue = newValue, CreatedAt = DateTime.UtcNow });
}