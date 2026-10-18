using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Models.Entities;

public class RequestDocument
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public OfficeAccountRequest Request { get; set; } = null!;
    public DocumentType Type { get; set; }
    public string FileUrl { get; set; } = string.Empty;
}