using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Models.Entities;

public class Property
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }
    public User Client { get; set; } = null!;

    public PropertyType Type { get; set; }
    public decimal AreaM2 { get; set; }
    public int? Floor { get; set; }
    public int? Bedrooms { get; set; }
    public int? Bathrooms { get; set; }
    public int? BuildingAge { get; set; }
    public bool HasParking { get; set; }
    public bool HasElevator { get; set; }

    public string Address { get; set; } = string.Empty;
    public decimal? Lat { get; set; }
    public decimal? Lng { get; set; }

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}