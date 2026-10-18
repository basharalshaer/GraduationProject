using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Models.Entities;

public class InspectionReport
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;

    public Guid EngineerId { get; set; }
    public Engineer Engineer { get; set; } = null!;

    public string ReportNo { get; set; } = string.Empty;
    public int? OverallScore { get; set; }          // empty while the report is a draft
    public string? Summary { get; set; }
    public ReportStatus Status { get; set; } = ReportStatus.Draft;
    public DateTime? IssuedAt { get; set; }
    public string? PdfUrl { get; set; }

    public ICollection<ReportSection> Sections { get; set; } = new List<ReportSection>();
}