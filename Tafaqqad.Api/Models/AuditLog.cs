namespace Tafaqqad.Api.Models.Entities;

public class AuditLog
{
    public Guid Id { get; set; }

    public Guid ActorUserId { get; set; }
    public User ActorUser { get; set; } = null!;

    public Guid? OfficeId { get; set; }
    public Office? Office { get; set; }

    public string Action { get; set; } = string.Empty;   // e.g. "PriceChanged", "EngineerAdded"
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}