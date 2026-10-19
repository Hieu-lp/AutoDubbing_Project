namespace AutoDubbing.Service.Routes;

public interface IWalkingRouteProvider
{
    Task<WalkingRouteResult> GetRouteAsync(IReadOnlyList<RouteStartDto> waypoints,
        CancellationToken cancellationToken);
}

public enum WalkingRouteFailure
{
    NotConfigured, QuotaExceeded, Timeout, NoRoute, Rejected, Unauthorized, Unavailable, InvalidResponse
}

public sealed record WalkingLeg(double DistanceMeters, double DurationSeconds);
public sealed record GeoJsonLineString(string Type, IReadOnlyList<double[]> Coordinates);
public sealed record WalkingRoute(double DistanceMeters, double DurationSeconds,
    GeoJsonLineString Geometry, IReadOnlyList<WalkingLeg> Legs);
public sealed record WalkingRouteResult(WalkingRoute? Route, WalkingRouteFailure? Failure)
{
    public static WalkingRouteResult Success(WalkingRoute route) => new(route, null);
    public static WalkingRouteResult Fail(WalkingRouteFailure failure) => new(null, failure);
}
