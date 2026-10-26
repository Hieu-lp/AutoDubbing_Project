namespace AutoDubbing.Data.Entities;

public class PoiTranslation
{
    public int Id { get; set; }
    public int PointOfInterestId { get; set; }
    public string Language { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public string? SourceVersion { get; set; }

    public PointOfInterest PointOfInterest { get; set; } = null!;
}
