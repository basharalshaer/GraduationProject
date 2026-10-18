namespace Tafaqqad.Api.Models.Entities;

public class SectionType
{
    public Guid Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public decimal Weight { get; set; }      // used to calculate the overall score
    public int SortOrder { get; set; }
}