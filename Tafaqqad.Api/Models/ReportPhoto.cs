namespace Tafaqqad.Api.Models.Entities;

public class ReportPhoto
{
    public Guid Id { get; set; }

    public Guid ReportSectionId { get; set; }
    public ReportSection ReportSection { get; set; } = null!;

    public string FileUrl { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public int SortOrder { get; set; }
}