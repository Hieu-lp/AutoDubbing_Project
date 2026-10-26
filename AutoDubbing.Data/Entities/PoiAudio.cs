namespace AutoDubbing.Data.Entities;

public class PoiAudio
{
    public int Id { get; set; }
    public int PointOfInterestId { get; set; }
    public string Language { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int Duration { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? SourceVersion { get; set; }
    public string? TextHash { get; set; }

    public PointOfInterest PointOfInterest { get; set; } = null!;
}
