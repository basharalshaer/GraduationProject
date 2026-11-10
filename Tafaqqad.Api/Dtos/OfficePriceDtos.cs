using System.ComponentModel.DataAnnotations;

namespace Tafaqqad.Api.Dtos;

public class OfficePriceDto
{
    public Guid Id { get; set; }
    public string PropertyType { get; set; } = string.Empty;
    public string? SizeBand { get; set; }
    public decimal Price { get; set; }
}

public class OfficePriceItem
{
    [Required] public string PropertyType { get; set; } = string.Empty;   // Apartment, Villa, Commercial, Land
    [MaxLength(60)] public string? SizeBand { get; set; }
    [Range(1, 10000)] public decimal Price { get; set; }
}

public class UpdateOfficePricesRequest
{
    [Required, MaxLength(20)]
    public List<OfficePriceItem> Items { get; set; } = new();
}