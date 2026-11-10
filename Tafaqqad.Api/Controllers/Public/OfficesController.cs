using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Dtos;
using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Controllers.Public;

[ApiController]
[AllowAnonymous]
[Route("api/offices")]
public class OfficesController : ControllerBase
{
    private readonly AppDbContext _db;
    public OfficesController(AppDbContext db) => _db = db;

    // GET api/offices?city=&propertyType=&maxPrice=&minRating=&search=&sort=rating|price|newest&page=1&pageSize=12
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? city, [FromQuery] string? propertyType, [FromQuery] decimal? maxPrice,
        [FromQuery] decimal? minRating, [FromQuery] string? search, [FromQuery] string? sort,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        PropertyType? type = null;
        if (!string.IsNullOrWhiteSpace(propertyType))
        {
            if (!Enum.TryParse<PropertyType>(propertyType, ignoreCase: true, out var t) || !Enum.IsDefined(t))
                return BadRequest(new { message = $"Unknown property type '{propertyType}'." });
            type = t;
        }

        // Only offices that are live
        var offices = _db.Offices.AsNoTracking().Where(o => o.Status == OfficeStatus.Active);

        if (!string.IsNullOrWhiteSpace(city)) offices = offices.Where(o => o.City == city.Trim());
        if (!string.IsNullOrWhiteSpace(search)) offices = offices.Where(o => o.Name.Contains(search.Trim()));
        if (minRating is not null) offices = offices.Where(o => o.RatingAvg >= minRating);

        var rows = offices.Select(o => new
        {
            o.Id,
            o.Name,
            o.City,
            o.LogoUrl,
            o.RatingAvg,
            o.RatingCount,
            o.DailyCapacity,
            StartingPrice = _db.OfficePrices
                .Where(p => p.OfficeId == o.Id && (!type.HasValue || p.PropertyType == type.Value))
                .Min(p => (decimal?)p.Price),
            EngineerCount = _db.Engineers.Count(e => e.OfficeId == o.Id
                && e.Status == EngineerStatus.Active && e.User.Status == UserStatus.Active),
            // "newest" = when the office's request was approved; fall back to the id order
            ApprovedAt = _db.AuditLogs.Where(a => a.OfficeId == o.Id && a.Action == "OfficeActivated")
                .Max(a => (DateTime?)a.CreatedAt)
        });

        if (type.HasValue) rows = rows.Where(r => r.StartingPrice != null);              // must offer this type
        if (maxPrice is not null) rows = rows.Where(r => r.StartingPrice != null && r.StartingPrice <= maxPrice);

        rows = (sort ?? "rating").ToLowerInvariant() switch
        {
            "price" => rows.OrderBy(r => r.StartingPrice == null).ThenBy(r => r.StartingPrice).ThenByDescending(r => r.RatingAvg),
            "newest" => rows.OrderByDescending(r => r.ApprovedAt),
            _ => rows.OrderByDescending(r => r.RatingAvg).ThenByDescending(r => r.RatingCount).ThenBy(r => r.Name)
        };

        var total = await rows.CountAsync();
        var items = await rows.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var cities = await _db.Offices.AsNoTracking()
            .Where(o => o.Status == OfficeStatus.Active)
            .Select(o => o.City).Distinct().OrderBy(c => c).ToListAsync();

        return Ok(new PublicOfficeListResponse
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Cities = cities,
            Items = items.Select(r => new PublicOfficeListItemDto
            {
                Id = r.Id,
                Name = r.Name,
                City = r.City,
                LogoUrl = r.LogoUrl,
                RatingAvg = r.RatingAvg,
                RatingCount = r.RatingCount,
                EngineerCount = r.EngineerCount,
                DailyCapacity = r.DailyCapacity,
                StartingPrice = r.StartingPrice
            }).ToList()
        });
    }
}