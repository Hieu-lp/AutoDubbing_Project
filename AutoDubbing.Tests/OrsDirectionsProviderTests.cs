using System.Net;
using System.Text.Json;
using AutoDubbing.Service.Routes;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoDubbing.Tests;

public sealed class OrsDirectionsProviderTests
{
    private const string TestKey = "test-only-ors-provider-key";
    private static readonly RouteStartDto[] Waypoints = [new(10, 106), new(10.1, 106.1)];
    internal const string GoodGeoJson = """
        {"type":"FeatureCollection","features":[{"type":"Feature",
         "properties":{"summary":{"distance":1500.5,"duration":900.25},
                       "segments":[{"distance":1500.5,"duration":900.25}]},
         "geometry":{"type":"LineString","coordinates":[[106,10],[106.05,10.03],[106.1,10.1]]}}],
         "metadata":{"query":{"api_key":"test-only-ors-provider-key"}}}
        """;

    [Fact]
    public async Task Posts_fixed_walking_geojson_endpoint_with_lng_lat_and_returns_only_safe_route_fields()
    {
        using var handler = new OrsHttpStub
        {
            Respond = (request, _) =>
            {
                Assert.Equal("application/geo+json", Assert.Single(request.Headers.Accept).MediaType);
                return Task.FromResult(OrsHttpStub.Json(HttpStatusCode.OK, GoodGeoJson));
            }
        };
        using var client = new HttpClient(handler);
        var result = await Create(client).GetRouteAsync(Waypoints, CancellationToken.None);
        Assert.Null(result.Failure);
        var route = Assert.IsType<WalkingRoute>(result.Route);
        Assert.Equal(1500.5, route.DistanceMeters);
        Assert.Equal(900.25, route.DurationSeconds);
        Assert.Equal(3, route.Geometry.Coordinates.Count);
        Assert.Equal(new[] { 106.05, 10.03 }, route.Geometry.Coordinates[1]);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(OrsDirectionsProvider.DirectionsUrl, sent.Url);
        Assert.Equal("POST", sent.Method);
        Assert.Equal(TestKey, sent.Authorization);
        Assert.DoesNotContain(TestKey, sent.Url!);
        Assert.DoesNotContain(TestKey, sent.Body);
        using var body = JsonDocument.Parse(sent.Body);
        var coordinates = body.RootElement.GetProperty("coordinates");
        Assert.Equal(new[] { 106d, 10d }, coordinates[0].EnumerateArray().Select(number => number.GetDouble()));
        Assert.Equal(new[] { 106.1, 10.1 }, coordinates[1].EnumerateArray().Select(number => number.GetDouble()));
        Assert.Equal("m", body.RootElement.GetProperty("units").GetString());
        Assert.True(body.RootElement.GetProperty("instructions").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("optimize_waypoints", out _));
        var output = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(TestKey, output);
        Assert.DoesNotContain("metadata", output, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("key\r\nHeader: value")]
    [InlineData("key with spaces")]
    public async Task Missing_or_invalid_key_fails_without_http(string? apiKey)
    {
        using var handler = new OrsHttpStub();
        using var client = new HttpClient(handler);
        var provider = new OrsDirectionsProvider(client, Options.Create(new OrsDirectionsOptions { ApiKey = apiKey }));
        var result = await provider.GetRouteAsync(Waypoints, CancellationToken.None);
        Assert.Equal(WalkingRouteFailure.NotConfigured, result.Failure);
        Assert.Null(result.Route);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Raw_api_key_with_base64_padding_is_sent_without_bearer_prefix_or_header_parse_errors()
    {
        using var handler = new OrsHttpStub();
        using var client = new HttpClient(handler);
        var provider = new OrsDirectionsProvider(client,
            Options.Create(new OrsDirectionsOptions { ApiKey = "test-only-base64-key==" }));
        var result = await provider.GetRouteAsync(Waypoints, CancellationToken.None);
        Assert.NotNull(result.Route);
        Assert.Equal("test-only-base64-key==", Assert.Single(handler.Requests).Authorization);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    public async Task Invalid_timeout_is_a_configuration_failure(int timeout)
    {
        using var handler = new OrsHttpStub();
        using var client = new HttpClient(handler);
        var result = await new OrsDirectionsProvider(client, Options.Create(new OrsDirectionsOptions
            { ApiKey = TestKey, TimeoutSeconds = timeout })).GetRouteAsync(Waypoints, CancellationToken.None);
        Assert.Equal(WalkingRouteFailure.NotConfigured, result.Failure);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(429, "{}", WalkingRouteFailure.QuotaExceeded)]
    [InlineData(403, "{\"error\":\"Quota exceeded\"}", WalkingRouteFailure.QuotaExceeded)]
    [InlineData(403, "{\"error\":{\"message\":\"Daily quota reached\"}}", WalkingRouteFailure.QuotaExceeded)]
    [InlineData(403, "{\"error\":\"Access disallowed\"}", WalkingRouteFailure.Unauthorized)]
    [InlineData(401, "{}", WalkingRouteFailure.Unauthorized)]
    [InlineData(404, "{\"error\":{\"code\":2009}}", WalkingRouteFailure.NoRoute)]
    [InlineData(400, "{\"error\":{\"code\":2010}}", WalkingRouteFailure.NoRoute)]
    [InlineData(400, "{\"error\":{\"code\":\"wrong-type\"}}", WalkingRouteFailure.Rejected)]
    [InlineData(413, "{}", WalkingRouteFailure.Rejected)]
    [InlineData(400, "{\"error\":{\"code\":2004}}", WalkingRouteFailure.Rejected)]
    [InlineData(500, "<html>server error</html>", WalkingRouteFailure.Unavailable)]
    [InlineData(503, "{}", WalkingRouteFailure.Unavailable)]
    [InlineData(302, "{}", WalkingRouteFailure.Unavailable)]
    public async Task Upstream_errors_are_classified_without_retries_or_fake_route(int status, string json,
        WalkingRouteFailure expected)
    {
        using var handler = new OrsHttpStub
        {
            Respond = (_, _) => Task.FromResult(OrsHttpStub.Json((HttpStatusCode)status, json))
        };
        using var client = new HttpClient(handler);
        var result = await Create(client).GetRouteAsync(Waypoints, CancellationToken.None);
        Assert.Equal(expected, result.Failure);
        Assert.Null(result.Route);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"type\":\"FeatureCollection\",\"features\":[null]}")]
    [InlineData("{\"type\":\"FeatureCollection\",\"features\":[{},{}]}")]
    public async Task Malformed_response_is_rejected(string json)
    {
        await AssertInvalidAsync(json);
    }

    [Theory]
    [InlineData("1500.5", "-1")]
    [InlineData("900.25", "1e999")]
    [InlineData("LineString", "Polygon")]
    [InlineData("[106,10]", "[10,106]")]
    [InlineData("[106.05,10.03]", "[181,10.03]")]
    [InlineData("[106.05,10.03]", "null")]
    [InlineData("[106.05,10.03]", "[106.05]")]
    [InlineData("[106.05,10.03]", "[106.05,10.03,9]")]
    [InlineData("\"segments\":[{\"distance\":1500.5,\"duration\":900.25}]", "\"segments\":[]")]
    public async Task Invalid_metrics_geometry_or_segment_count_are_rejected(string from, string to)
    {
        await AssertInvalidAsync(GoodGeoJson.Replace(from, to, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inconsistent_summary_is_rejected()
    {
        await AssertInvalidAsync(GoodGeoJson.Replace("\"summary\":{\"distance\":1500.5", "\"summary\":{\"distance\":9999"));
    }

    [Fact]
    public async Task Empty_feature_collection_reports_no_route()
    {
        using var handler = new OrsHttpStub
        {
            Respond = (_, _) => Task.FromResult(OrsHttpStub.Json(HttpStatusCode.OK,
                "{\"type\":\"FeatureCollection\",\"features\":[]}"))
        };
        using var client = new HttpClient(handler);
        var result = await Create(client).GetRouteAsync(Waypoints, CancellationToken.None);
        Assert.Equal(WalkingRouteFailure.NoRoute, result.Failure);
        Assert.Null(result.Route);
    }

    [Fact]
    public async Task Transport_failure_does_not_propagate_exception_details_or_key()
    {
        using var handler = new OrsHttpStub
        {
            Respond = (_, _) => throw new HttpRequestException("private transport info " + TestKey)
        };
        using var client = new HttpClient(handler);
        var result = await Create(client).GetRouteAsync(Waypoints, CancellationToken.None);
        Assert.Equal(WalkingRouteFailure.Unavailable, result.Failure);
        Assert.DoesNotContain(TestKey, JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task Provider_timeout_is_distinct_from_caller_cancellation()
    {
        using var handler = new OrsHttpStub { Respond = async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        } };
        using var client = new HttpClient(handler);
        var result = await Create(client, timeoutSeconds: 1).GetRouteAsync(Waypoints, CancellationToken.None);
        Assert.Equal(WalkingRouteFailure.Timeout, result.Failure);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(client).GetRouteAsync(Waypoints, cancellation.Token));
    }

    [Fact]
    public async Task Timeout_also_covers_reading_body_after_headers()
    {
        using var handler = new OrsHttpStub
        {
            Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new SlowBody() })
        };
        using var client = new HttpClient(handler);
        var result = await Create(client, timeoutSeconds: 1).GetRouteAsync(Waypoints, CancellationToken.None);
        Assert.Equal(WalkingRouteFailure.Timeout, result.Failure);
        Assert.Null(result.Route);
    }

    // Ca thành công/parse không đo tốc độ JIT trên máy CI; các ca timeout truyền 1 giây riêng.
    private static OrsDirectionsProvider Create(HttpClient client, int timeoutSeconds = 10) => new(client,
        Options.Create(new OrsDirectionsOptions { ApiKey = TestKey, TimeoutSeconds = timeoutSeconds }));

    private static async Task AssertInvalidAsync(string json)
    {
        using var handler = new OrsHttpStub { Respond = (_, _) =>
            Task.FromResult(OrsHttpStub.Json(HttpStatusCode.OK, json)) };
        using var client = new HttpClient(handler);
        var result = await Create(client).GetRouteAsync(Waypoints, CancellationToken.None);
        Assert.Equal(WalkingRouteFailure.InvalidResponse, result.Failure);
        Assert.Null(result.Route);
    }

    private sealed class SlowBody : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context,
            CancellationToken cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
