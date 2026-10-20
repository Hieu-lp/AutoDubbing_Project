using System.ComponentModel.DataAnnotations;
using AutoDubbing.Service.Validation;

namespace AutoDubbing.Service.Routes;

public sealed class RoutePreviewRequest : IValidatableObject
{
    [Required(ErrorMessage = "Thiếu startLatitude.")]
    [FiniteRange(-90, 90)]
    public double? StartLatitude { get; init; }

    [Required(ErrorMessage = "Thiếu startLongitude.")]
    [FiniteRange(-180, 180)]
    public double? StartLongitude { get; init; }

    [Required(ErrorMessage = "Thiếu poiIds.")]
    [MinLength(1, ErrorMessage = "poiIds không được rỗng.")]
    [MaxLength(OrsDirectionsOptions.MaxPoiCount, ErrorMessage = "ORS hỗ trợ tối đa 49 POI và một điểm xuất phát.")]
    public int[]? PoiIds { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (PoiIds is null) { yield break; }
        if (PoiIds.Any(id => id <= 0))
        {
            yield return new ValidationResult("Mọi poiIds phải là số nguyên dương.", [nameof(PoiIds)]);
        }
        if (PoiIds.Distinct().Count() != PoiIds.Length)
        {
            yield return new ValidationResult("poiIds chứa ID trùng; hãy gửi mỗi ID đúng một lần.", [nameof(PoiIds)]);
        }
    }
}

public sealed record RouteStartDto(double Latitude, double Longitude);
public sealed record RouteStopDto(int Order, int PoiId, string Name, double Latitude, double Longitude,
    double DistanceFromPreviousMeters, double DurationFromPreviousSeconds);
public sealed record RoutePreviewDto(RouteStartDto Start, IReadOnlyList<RouteStopDto> Stops,
    double TotalDistanceMeters, double TotalDurationSeconds, GeoJsonLineString Geometry,
    string Algorithm = "nearestNeighbor", string DistanceType = "walking", string Profile = "foot-walking",
    string Provider = "openrouteservice",
    string Attribution = "© openrouteservice.org by HeiGIT | Map data © OpenStreetMap contributors");
public sealed record RoutePreviewResult(RoutePreviewDto? Preview, IReadOnlyList<int> UnavailablePoiIds,
    WalkingRouteFailure? RoutingFailure = null);
