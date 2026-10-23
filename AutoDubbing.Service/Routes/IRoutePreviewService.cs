namespace AutoDubbing.Service.Routes;

public interface IRoutePreviewService
{
    Task<RoutePreviewResult> PreviewAsync(double startLatitude, double startLongitude, int[] poiIds,
        CancellationToken cancellationToken);
}
