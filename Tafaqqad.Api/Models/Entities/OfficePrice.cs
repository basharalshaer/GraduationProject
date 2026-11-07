using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Models.Entities;

public class OfficePrice
{
    public Guid Id { get; set; }
    public Guid OfficeId { get; set; }
    public Office Office { get; set; } = null!;

    public PropertyType PropertyType { get; set; }
    public string SizeBand { get; set; } = string.Empty;   // e.g. "up to 120 m2"
    public decimal Price { get; set; }
}