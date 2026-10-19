namespace AutoDubbing.Service.Routes;

public sealed class OrsDirectionsOptions
{
    public const string SectionName = "OpenRouteService";
    // Public Directions service accepts at most 50 waypoints including the start.
    public const int MaxPoiCount = 49;
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 20;
}
