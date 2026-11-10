using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tafaqqad.Api.Data;
using Tafaqqad.Api.Dtos;
using Tafaqqad.Api.Models.Enums;
using Tafaqqad.Api.Services;

namespace Tafaqqad.Api.Controllers.Public;

[ApiController]
[AllowAnonymous]
[Route("api/offices")]
public class OfficesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _files;

    public OfficesController(AppDbContext db, IFileStorage files)
    {
        _db = db;
        _files = files;
    }

    private static string? PublicLogoUrl(Guid officeId, string? storedPath) =>
        string.IsNullOrEmpty(storedPath) ? null : $"/api/offices/{officeId}/logo";

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
            ApprovedAt = _db.AuditLogs.Where(a => a.OfficeId == o.Id && a.Action == "OfficeActivated")
                .Max(a => (DateTime?)a.CreatedAt)
        });

        if (type.HasValue) rows = rows.Where(r => r.StartingPrice != null);
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
                LogoUrl = PublicLogoUrl(r.Id, r.LogoUrl),   // 7b: public URL, never the internal path
                RatingAvg = r.RatingAvg,
                RatingCount = r.RatingCount,
                EngineerCount = r.EngineerCount,
                DailyCapacity = r.DailyCapacity,
                StartingPrice = r.StartingPrice
            }).ToList()
        });
    }

    // GET api/offices/compare?ids=guid1,guid2,guid3
    [HttpGet("compare")]
    public async Task<IActionResult> Compare([FromQuery] string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
            return BadRequest(new { message = "Send 2 to 4 office ids, separated by commas." });

        var requested = new List<Guid>();
        foreach (var part in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out var g))
                return BadRequest(new { message = $"'{part}' is not a valid office id." });
            if (!requested.Contains(g)) requested.Add(g);   // remove duplicates, keep order
        }

        if (requested.Count < 2 || requested.Count > 4)
            return BadRequest(new { message = "You can compare 2 to 4 different offices." });

        var offices = await _db.Offices.AsNoTracking()
            .Include(o => o.Prices)
            .Include(o => o.WorkingHours)
            .AsSplitQuery()
            .Where(o => requested.Contains(o.Id) && o.Status == OfficeStatus.Active)
            .ToListAsync();

        var foundIds = offices.Select(o => o.Id).ToList();

        var engineerCounts = await _db.Engineers
            .Where(e => foundIds.Contains(e.OfficeId)
                && e.Status == EngineerStatus.Active && e.User.Status == UserStatus.Active)
            .GroupBy(e => e.OfficeId)
            .Select(g => new { OfficeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OfficeId, x => x.Count);

        var memberSince = await _db.AuditLogs
            .Where(a => a.OfficeId != null && foundIds.Contains(a.OfficeId.Value) && a.Action == "OfficeActivated")
            .GroupBy(a => a.OfficeId!.Value)
            .Select(g => new { OfficeId = g.Key, At = g.Max(a => a.CreatedAt) })
            .ToDictionaryAsync(x => x.OfficeId, x => x.At);

        var propertyTypes = offices
            .SelectMany(o => o.Prices.Select(p => p.PropertyType))
            .Distinct()
            .OrderBy(t => t)
            .Select(t => t.ToString())
            .ToList();

        var byId = offices.ToDictionary(o => o.Id);

        return Ok(new CompareOfficesResponse
        {
            PropertyTypes = propertyTypes,
            MissingIds = requested.Where(id => !byId.ContainsKey(id)).ToList(),
            Offices = requested.Where(byId.ContainsKey).Select(id =>
            {
                var o = byId[id];
                return new CompareOfficeDto
                {
                    Id = o.Id,
                    Name = o.Name,
                    City = o.City,
                    LogoUrl = PublicLogoUrl(o.Id, o.LogoUrl),
                    RatingAvg = o.RatingAvg,
                    RatingCount = o.RatingCount,
                    EngineerCount = engineerCounts.GetValueOrDefault(o.Id),
                    DailyCapacity = o.DailyCapacity,
                    OpenDays = o.WorkingHours.Count(h => !h.IsClosed),
                    StartingPrice = o.Prices.Count == 0 ? null : o.Prices.Min(p => p.Price),
                    MemberSince = memberSince.TryGetValue(o.Id, out var at) ? at : null,
                    CheapestByType = o.Prices
                        .GroupBy(p => p.PropertyType)
                        .ToDictionary(g => g.Key.ToString(), g => g.Min(p => p.Price))
                };
            }).ToList()
        });
    }

    // GET api/offices/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id)
    {
        var office = await _db.Offices.AsNoTracking()
            .Include(o => o.Prices)
            .Include(o => o.WorkingHours)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id && o.Status == OfficeStatus.Active);

        // Incomplete, Suspended or unknown: all look the same from outside
        if (office is null) return NotFound(new { message = "Office not found." });

        var engineerCount = await _db.Engineers.CountAsync(e => e.OfficeId == office.Id
            && e.Status == EngineerStatus.Active && e.User.Status == UserStatus.Active);

        var memberSince = await _db.AuditLogs
            .Where(a => a.OfficeId == office.Id && a.Action == "OfficeActivated")
            .MaxAsync(a => (DateTime?)a.CreatedAt);

        return Ok(new PublicOfficeDetailsDto
        {
            Id = office.Id,
            Name = office.Name,
            City = office.City,
            Description = office.Description,
            LogoUrl = PublicLogoUrl(office.Id, office.LogoUrl),
            RatingAvg = office.RatingAvg,
            RatingCount = office.RatingCount,
            EngineerCount = engineerCount,
            DailyCapacity = office.DailyCapacity,
            StartingPrice = office.Prices.Count == 0 ? null : office.Prices.Min(p => p.Price),
            MemberSince = memberSince,
            Prices = office.Prices
                .OrderBy(p => p.PropertyType).ThenBy(p => p.Price)
                .Select(p => new PublicOfficePriceDto
                {
                    PropertyType = p.PropertyType.ToString(),
                    SizeBand = string.IsNullOrEmpty(p.SizeBand) ? null : p.SizeBand,
                    Price = p.Price
                }).ToList(),
            // Week starts on Saturday (Saturday=0 ... Friday=6)
            WorkingHours = office.WorkingHours
                .OrderBy(h => ((int)h.Weekday + 1) % 7)
                .Select(h => new PublicWorkingHourDto
                {
                    Weekday = h.Weekday.ToString(),
                    IsClosed = h.IsClosed,
                    OpenTime = h.IsClosed ? null : h.OpenTime?.ToString("HH:mm"),
                    CloseTime = h.IsClosed ? null : h.CloseTime?.ToString("HH:mm")
                }).ToList()
        });
    }

    // GET api/offices/{id}/logo
    [HttpGet("{id:guid}/logo")]
    public async Task<IActionResult> Logo(Guid id)
    {
        var stored = await _db.Offices.AsNoTracking()
            .Where(o => o.Id == id && o.Status == OfficeStatus.Active)
            .Select(o => o.LogoUrl)
            .FirstOrDefaultAsync();
        if (string.IsNullOrEmpty(stored)) return NotFound();

        string? fullPath;
        try { fullPath = _files.ResolvePath(stored); }   // refuses anything outside Uploads/
        catch { return NotFound(); }
        if (string.IsNullOrEmpty(fullPath) || !System.IO.File.Exists(fullPath)) return NotFound();

        var contentType = DetectImageType(fullPath);      // real header, not the extension
        if (contentType is null) return NotFound();

        Response.Headers.CacheControl = "public, max-age=3600";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(fullPath, contentType);
    }

    private static string? DetectImageType(string path)
    {
        var header = new byte[4];
        using var stream = System.IO.File.OpenRead(path);
        if (stream.Read(header, 0, 4) < 4) return null;

        if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47) return "image/png";
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return "image/jpeg";
        return null;
    }
}