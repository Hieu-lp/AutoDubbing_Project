using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoDubbing.Service.Routes;
using Xunit;

namespace AutoDubbing.Tests;

public sealed class WalkingRouteApiTests
{
    [Fact]
    public async Task All_permutations_produce_identical_route_and_provider_request_including_id_ties()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var west = await fixture.AddPoiAsync(longitude: -1);
        var east = await fixture.AddPoiAsync(longitude: 1);
        var north = await fixture.AddPoiAsync(latitude: 1);
        string? baseline = null;
        foreach (var ids in Permutations(west, east, north))
        {
            using var response = await fixture.Client.PostAsJsonAsync("/api/routes/preview", new
            {
                startLatitude = 0, startLongitude = 0, poiIds = ids
            });
            response.EnsureSuccessStatusCode();
            var preview = await response.Content.ReadFromJsonAsync<RoutePreviewDto>();
            Assert.Equal(new[] { west, north, east }, preview!.Stops.Select(stop => stop.PoiId));
            var json = await response.Content.ReadAsStringAsync();
            baseline ??= json;
            Assert.Equal(baseline, json);
        }
        Assert.Equal(6, fixture.Factory.RoutingHttp.Requests.Count);
        Assert.Single(fixture.Factory.RoutingHttp.Requests.Select(request => request.Body).Distinct());
    }

    [Fact]
    public async Task Explicit_start_changes_order_and_is_always_first_lng_lat_sent_to_provider()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var west = await fixture.AddPoiAsync(longitude: -1);
        var east = await fixture.AddPoiAsync(longitude: 1);
        foreach (var startLongitude in new[] { -2d, 2d })
        {
            using var response = await fixture.Client.PostAsJsonAsync("/api/routes/preview", new
            {
                startLatitude = 0, startLongitude, poiIds = new[] { east, west }
            });
            response.EnsureSuccessStatusCode();
            var preview = await response.Content.ReadFromJsonAsync<RoutePreviewDto>();
            Assert.Equal(startLongitude < 0 ? west : east, preview!.Stops[0].PoiId);
            Assert.Equal(startLongitude, preview.Start.Longitude);
            Assert.Equal(0, preview.Start.Latitude);
            using var request = JsonDocument.Parse(fixture.Factory.RoutingHttp.Requests[^1].Body);
            Assert.Equal(new[] { startLongitude, 0d }, request.RootElement.GetProperty("coordinates")[0]
                .EnumerateArray().Select(number => number.GetDouble()));
        }
    }

    [Fact]
    public async Task Missing_key_returns_503_without_calling_provider_or_straight_line_fallback()
    {
        await using var fixture = await DemoApiFixture.CreateAsync(routingApiKey: null);
        var id = await fixture.AddPoiAsync(latitude: 10, longitude: 106);
        using var response = await fixture.Client.PostAsJsonAsync("/api/routes/preview", new
        {
            startLatitude = 10.1, startLongitude = 106.1, poiIds = new[] { id }
        });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("routing_not_configured", json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("geometry", out _));
        Assert.False(json.RootElement.TryGetProperty("totalDistanceMeters", out _));
        Assert.Empty(fixture.Factory.RoutingHttp.Requests);
        // Other visitor APIs still work when ORS is not configured.
        using var list = await fixture.Client.GetAsync("/api/pois");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Theory]
    [InlineData(429, "{\"error\":\"test-only-ors-key\"}", 503, "routing_quota_exceeded")]
    [InlineData(403, "{\"error\":\"Quota exceeded test-only-ors-key\"}", 503, "routing_quota_exceeded")]
    [InlineData(401, "{\"error\":\"test-only-ors-key\"}", 503, "routing_provider_rejected")]
    [InlineData(403, "{\"error\":\"Unauthorized test-only-ors-key\"}", 503, "routing_provider_rejected")]
    [InlineData(404, "{\"error\":{\"code\":2009,\"message\":\"test-only-ors-key\"}}", 422, "walking_route_not_found")]
    [InlineData(400, "{\"error\":{\"code\":2010}}", 422, "walking_route_not_found")]
    [InlineData(400, "{\"error\":{\"code\":2004}}", 422, "routing_request_rejected")]
    [InlineData(500, "{\"error\":\"test-only-ors-key\"}", 502, "routing_unavailable")]
    [InlineData(200, "not json test-only-ors-key", 502, "routing_invalid_response")]
    public async Task Provider_failures_return_safe_problem_details(int upstreamStatus, string upstreamBody,
        int expectedStatus, string code)
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        fixture.Factory.RoutingHttp.Respond = (_, _) =>
            Task.FromResult(OrsHttpStub.Json((HttpStatusCode)upstreamStatus, upstreamBody));
        var id = await fixture.AddPoiAsync(latitude: 10, longitude: 106);
        using var response = await fixture.Client.PostAsJsonAsync("/api/routes/preview", new
        {
            startLatitude = 10.1, startLongitude = 106.1, poiIds = new[] { id }
        });
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        var output = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(output);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("geometry", out _));
        Assert.DoesNotContain("test-only-ors-key", output);
        Assert.DoesNotContain("test-only-ors-key", string.Join('\n', fixture.Factory.CapturedLogs.Messages));
        Assert.Single(fixture.Factory.RoutingHttp.Requests);
    }

    [Fact]
    public async Task Timeout_returns_504_without_route_or_secret_in_logs()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        fixture.Factory.RoutingHttp.Respond = async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        };
        var id = await fixture.AddPoiAsync();
        using var response = await fixture.Client.PostAsJsonAsync("/api/routes/preview", new
        {
            startLatitude = 0, startLongitude = 0.1, poiIds = new[] { id }
        });
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("routing_timeout", json.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("test-only-ors-key", string.Join('\n', fixture.Factory.CapturedLogs.Messages));
    }

    [Fact]
    public async Task Successful_response_contains_provider_geometry_duration_attribution_and_no_key()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var id = await fixture.AddPoiAsync(latitude: 10.1, longitude: 106.1);
        fixture.Factory.RoutingHttp.Respond = (_, _) =>
            Task.FromResult(OrsHttpStub.Json(HttpStatusCode.OK, OrsDirectionsProviderTests.GoodGeoJson));
        using var response = await fixture.Client.PostAsJsonAsync("/api/routes/preview", new
        {
            startLatitude = 10, startLongitude = 106, poiIds = new[] { id }
        });
        response.EnsureSuccessStatusCode();
        var output = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("test-only-ors-key", output);
        Assert.DoesNotContain("test-only-ors-provider-key", output);
        Assert.DoesNotContain("metadata", output);
        var preview = await response.Content.ReadFromJsonAsync<RoutePreviewDto>();
        Assert.Equal(1500.5, preview!.TotalDistanceMeters);
        Assert.Equal(900.25, preview.TotalDurationSeconds);
        Assert.Equal(new[] { 106.05, 10.03 }, preview.Geometry.Coordinates[1]);
        Assert.Contains("HeiGIT", preview.Attribution);
        Assert.Contains("OpenStreetMap", preview.Attribution);
        Assert.DoesNotContain("test-only-ors-key", string.Join('\n', fixture.Factory.CapturedLogs.Messages));
    }

    [Fact]
    public async Task More_than_49_pois_is_validation_error_before_database_or_provider()
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync("/api/routes/preview", new
        {
            startLatitude = 0, startLongitude = 0, poiIds = Enumerable.Range(1, 50).ToArray()
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.RoutingHttp.Requests);
    }

    internal static int[][] Permutations(int a, int b, int c) =>
        [[a, b, c], [a, c, b], [b, a, c], [b, c, a], [c, a, b], [c, b, a]];
}
