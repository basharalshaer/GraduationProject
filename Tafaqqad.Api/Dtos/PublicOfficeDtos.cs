namespace Tafaqqad.Api.Dtos;

public class PublicOfficeListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public decimal RatingAvg { get; set; }
    public int RatingCount { get; set; }
    public int EngineerCount { get; set; }
    public int DailyCapacity { get; set; }
    public decimal? StartingPrice { get; set; }   // cheapest price (for the chosen property type, if any)
}

public class PagedResult<T>
{
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<T> Items { get; set; } = new();
}

public class PublicOfficeListResponse : PagedResult<PublicOfficeListItemDto>
{
    public List<string> Cities { get; set; } = new();   // for the city filter dropdown
}

public class PublicOfficePriceDto
{
    public string PropertyType { get; set; } = string.Empty;
    public string? SizeBand { get; set; }
    public decimal Price { get; set; }
}

public class PublicWorkingHourDto
{
    public string Weekday { get; set; } = string.Empty;   // "Saturday", "Sunday", ...
    public bool IsClosed { get; set; }
    public string? OpenTime { get; set; }                 // "HH:mm", null when closed
    public string? CloseTime { get; set; }
}

public class PublicOfficeDetailsDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public decimal RatingAvg { get; set; }
    public int RatingCount { get; set; }
    public int EngineerCount { get; set; }
    public int DailyCapacity { get; set; }
    public decimal? StartingPrice { get; set; }
    public DateTime? MemberSince { get; set; }            // when the office went live
    public List<PublicOfficePriceDto> Prices { get; set; } = new();
    public List<PublicWorkingHourDto> WorkingHours { get; set; } = new();
}