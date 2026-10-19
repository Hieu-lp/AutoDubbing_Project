namespace AutoDubbing.Data.Entities;

public class UsageEvent
{
    public int Id { get; set; }
    public int PointOfInterestId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }

    public PointOfInterest PointOfInterest { get; set; } = null!;
}
