using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoDubbing.Service.Geography;
using Microsoft.Extensions.Options;

namespace AutoDubbing.Service.Routes;

public sealed class OrsDirectionsProvider(HttpClient httpClient, IOptions<OrsDirectionsOptions> options)
    : IWalkingRouteProvider
{
    public const string DirectionsUrl =
        "https://api.heigit.org/openrouteservice/v2/directions/foot-walking/geojson";
    private const long MaxResponseBytes = 8 * 1024 * 1024;

    public async Task<WalkingRouteResult> GetRouteAsync(IReadOnlyList<RouteStartDto> waypoints,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || settings.ApiKey.Any(character => character <= 32 || character > 126)
            || settings.TimeoutSeconds is < 1 or > 120)
        {
            return WalkingRouteResult.Fail(WalkingRouteFailure.NotConfigured);
        }
        if (waypoints.Count is < 2 or > 50 || waypoints.Any(point =>
                !GeoMath.IsLatitude(point.Latitude) || !GeoMath.IsLongitude(point.Longitude)))
        {
            return WalkingRouteResult.Fail(WalkingRouteFailure.Rejected);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Post, DirectionsUrl);
        // Keep credentials out of URLs, JSON, DTOs, logs and provider error messages.
        request.Headers.TryAddWithoutValidation("Authorization", settings.ApiKey);
        request.Headers.Accept.ParseAdd("application/geo+json");
        request.Content = JsonContent.Create(new
        {
            coordinates = waypoints.Select(point => new[] { point.Longitude, point.Latitude }),
            // ORS omits segment metrics when instructions are disabled. Discard steps in our response.
            units = "m", instructions = true, elevation = false
        });

        try
        {
            using var response = await httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            // Apply the same timeout to headers and the response body, and bound buffering.
            await response.Content.LoadIntoBufferAsync(MaxResponseBytes, timeout.Token);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return await ReadFailureAsync(response.StatusCode, stream, timeout.Token);
            }
            using var json = await JsonDocument.ParseAsync(stream,
                new JsonDocumentOptions { MaxDepth = 32 }, timeout.Token);
            return ParseRoute(json.RootElement, waypoints.Count - 1);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WalkingRouteResult.Fail(WalkingRouteFailure.Timeout);
        }
        catch (JsonException) { return WalkingRouteResult.Fail(WalkingRouteFailure.InvalidResponse); }
        catch (HttpRequestException) { return WalkingRouteResult.Fail(WalkingRouteFailure.Unavailable); }
        catch (IOException) { return WalkingRouteResult.Fail(WalkingRouteFailure.Unavailable); }
    }

    private static async Task<WalkingRouteResult> ReadFailureAsync(HttpStatusCode status, Stream stream,
        CancellationToken cancellationToken)
    {
        if (status == HttpStatusCode.TooManyRequests)
        {
            return WalkingRouteResult.Fail(WalkingRouteFailure.QuotaExceeded);
        }
        var noRoute = false;
        var quota = false;
        try
        {
            using var json = await JsonDocument.ParseAsync(stream,
                new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken);
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.Object)
                {
                    noRoute = error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number
                        && code.TryGetInt32(out var number) && number is 2009 or 2010;
                    if (error.TryGetProperty("message", out var message)) { error = message; }
                }
                quota = error.ValueKind == JsonValueKind.String
                    && error.GetString()!.Contains("quota", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (JsonException) { /* Do not expose an upstream error body, including non-JSON errors. */ }

        var failure = status switch
        {
            HttpStatusCode.Forbidden when quota => WalkingRouteFailure.QuotaExceeded,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => WalkingRouteFailure.Unauthorized,
            _ when noRoute => WalkingRouteFailure.NoRoute,
            HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge => WalkingRouteFailure.Rejected,
            _ => WalkingRouteFailure.Unavailable
        };
        return WalkingRouteResult.Fail(failure);
    }

    private static WalkingRouteResult ParseRoute(JsonElement root, int expectedLegs)
    {
        if (!HasType(root, "FeatureCollection") || !root.TryGetProperty("features", out var features)
            || features.ValueKind != JsonValueKind.Array)
        {
            return WalkingRouteResult.Fail(WalkingRouteFailure.InvalidResponse);
        }
        if (features.GetArrayLength() == 0) { return WalkingRouteResult.Fail(WalkingRouteFailure.NoRoute); }
        if (features.GetArrayLength() != 1) { return WalkingRouteResult.Fail(WalkingRouteFailure.InvalidResponse); }
        var feature = features[0];
        if (!HasType(feature, "Feature") || !feature.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object
            || !properties.TryGetProperty("summary", out var summary)
            || !ReadMetric(summary, "distance", out var distance) || !ReadMetric(summary, "duration", out var duration)
            || !properties.TryGetProperty("segments", out var segments)
            || segments.ValueKind != JsonValueKind.Array || segments.GetArrayLength() != expectedLegs
            || !feature.TryGetProperty("geometry", out var geometry) || !HasType(geometry, "LineString")
            || !geometry.TryGetProperty("coordinates", out var coordinates)
            || coordinates.ValueKind != JsonValueKind.Array || coordinates.GetArrayLength() < 2)
        {
            return WalkingRouteResult.Fail(WalkingRouteFailure.InvalidResponse);
        }
        var legs = new List<WalkingLeg>(expectedLegs);
        foreach (var segment in segments.EnumerateArray())
        {
            if (!ReadMetric(segment, "distance", out var legDistance)
                || !ReadMetric(segment, "duration", out var legDuration))
            {
                return WalkingRouteResult.Fail(WalkingRouteFailure.InvalidResponse);
            }
            legs.Add(new WalkingLeg(legDistance, legDuration));
        }
        // ORS rounds segment and summary values independently.
        var tolerance = Math.Max(1d, expectedLegs * 0.1d);
        if (Math.Abs(legs.Sum(leg => leg.DistanceMeters) - distance) > tolerance
            || Math.Abs(legs.Sum(leg => leg.DurationSeconds) - duration) > tolerance)
        {
            return WalkingRouteResult.Fail(WalkingRouteFailure.InvalidResponse);
        }
        var positions = new List<double[]>(coordinates.GetArrayLength());
        foreach (var position in coordinates.EnumerateArray())
        {
            if (position.ValueKind != JsonValueKind.Array || position.GetArrayLength() != 2
                || !ReadNumber(position[0], out var longitude) || !GeoMath.IsLongitude(longitude)
                || !ReadNumber(position[1], out var latitude) || !GeoMath.IsLatitude(latitude))
            {
                return WalkingRouteResult.Fail(WalkingRouteFailure.InvalidResponse);
            }
            positions.Add([longitude, latitude]);
        }
        // Copy only route data; never return metadata.query, error messages or other upstream fields.
        return WalkingRouteResult.Success(new WalkingRoute(distance, duration,
            new GeoJsonLineString("LineString", positions), legs));
    }

    private static bool HasType(JsonElement element, string type) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty("type", out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() == type;

    private static bool ReadMetric(JsonElement element, string name, out double value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var number)
            && ReadNumber(number, out value) && value >= 0;
    }

    private static bool ReadNumber(JsonElement element, out double value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value) && double.IsFinite(value);
    }
}
