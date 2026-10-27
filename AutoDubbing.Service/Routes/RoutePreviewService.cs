using AutoDubbing.Data.Repositories;
using AutoDubbing.Data.Entities;
using AutoDubbing.Service.Geography;

namespace AutoDubbing.Service.Routes;

public sealed class RoutePreviewService(IPoiReadRepository repository, IWalkingRouteProvider walkingProvider)
    : IRoutePreviewService
{
    public async Task<RoutePreviewResult> PreviewAsync(double startLatitude, double startLongitude,
        int[] poiIds, CancellationToken cancellationToken, bool preserveOrder = false)
    {
        if (!GeoMath.IsLatitude(startLatitude) || !GeoMath.IsLongitude(startLongitude)
            || poiIds.Length is < 1 or > OrsDirectionsOptions.MaxPoiCount
            || poiIds.Any(id => id <= 0) || poiIds.Distinct().Count() != poiIds.Length)
        {
            throw new ArgumentException("Tọa độ hoặc danh sách POI không hợp lệ.");
        }

        var remaining = await repository.FindPublishedByIdsAsync(poiIds, cancellationToken);
        var foundIds = remaining.Select(poi => poi.Id).ToHashSet();
        var missing = poiIds.Where(id => !foundIds.Contains(id)).OrderBy(id => id).ToArray();
        if (missing.Length > 0) { return new RoutePreviewResult(null, missing); }

        var ordered = new List<PointOfInterest>(remaining.Count);
        var latitude = startLatitude;
        var longitude = startLongitude;
        while (remaining.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bestIndex = 0;
            var bestDistance = double.PositiveInfinity;
            for (var index = 0; index < remaining.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var poi = remaining[index];
                var distance = GeoMath.DistanceMeters(latitude, longitude, poi.Latitude, poi.Longitude);
                if (preserveOrder ? poi.Id == poiIds[ordered.Count] : distance < bestDistance || distance == bestDistance && poi.Id < remaining[bestIndex].Id)
                {
                    bestIndex = index;
                    bestDistance = distance;
                }
            }
            var next = remaining[bestIndex];
            ordered.Add(next);
            latitude = next.Latitude;
            longitude = next.Longitude;
            remaining.RemoveAt(bestIndex);
        }
        var start = new RouteStartDto(startLatitude, startLongitude);
        var waypoints = new[] { start }.Concat(ordered.Select(poi => new RouteStartDto(poi.Latitude, poi.Longitude)))
            .ToArray();
        var result = await walkingProvider.GetRouteAsync(waypoints, cancellationToken);
        if (result.Route is null)
        {
            return new RoutePreviewResult(null, [], result.Failure ?? WalkingRouteFailure.InvalidResponse);
        }
        var route = result.Route;
        var stops = ordered.Select((poi, index) => new RouteStopDto(index + 1, poi.Id, poi.Name,
            poi.Latitude, poi.Longitude, route.Legs[index].DistanceMeters, route.Legs[index].DurationSeconds)).ToArray();
        return new RoutePreviewResult(new RoutePreviewDto(start, stops, route.DistanceMeters,
            route.DurationSeconds, route.Geometry, Algorithm: preserveOrder ? "adminOrder" : "nearestNeighbor"), []);
    }
}
