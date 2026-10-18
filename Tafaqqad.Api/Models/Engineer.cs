using Tafaqqad.Api.Models.Enums;

namespace Tafaqqad.Api.Models.Entities;

public class Engineer
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }          // the login; the name lives in User.FullName
    public User User { get; set; } = null!;

    public Guid OfficeId { get; set; }
    public Office Office { get; set; } = null!;

    public string Specialty { get; set; } = string.Empty;
    public int YearsExperience { get; set; }
    public string SyndicateNo { get; set; } = string.Empty;
    public EngineerStatus Status { get; set; } = EngineerStatus.Active;
}