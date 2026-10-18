using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Models.Entities;

public class ReportSection
{
    public Guid Id { get; set; }

    public Guid ReportId { get; set; }
    public InspectionReport Report { get; set; } = null!;

    public Guid SectionTypeId { get; set; }
    public SectionType SectionType { get; set; } = null!;

    public SectionRating Rating { get; set; } = SectionRating.Ok;
    public string? Notes { get; set; }

    public ICollection<ReportPhoto> Photos { get; set; } = new List<ReportPhoto>();
}