using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AutoDubbing.Data.Contexts;
using AutoDubbing.Data.Entities;
using AutoDubbing.Service.Routes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoDubbing.Tests;

public sealed class RoutePreviewTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"startLatitude\":0,\"startLongitude\":0}")]
    [InlineData("{\"startLatitude\":0,\"startLongitude\":0,\"poiIds\":null}")]
    [InlineData("{\"startLatitude\":0,\"startLongitude\":0,\"poiIds\":[]}")]
    [InlineData("{\"startLatitude\":0,\"startLongitude\":0,\"poiIds\":[1,1]}")]
    [InlineData("{\"startLatitude\":0,\"startLongitude\":0,\"poiIds\":[0]}")]
    [InlineData("{\"startLatitude\":0,\"startLongitude\":0,\"poiIds\":[-1]}")]
    [InlineData("{\"startLongitude\":0,\"poiIds\":[1]}")]
    [InlineData("{\"startLatitude\":0,\"poiIds\":[1]}")]
    [InlineData("{\"startLatitude\":91,\"startLongitude\":0,\"poiIds\":[1]}")]
    [InlineData("{\"startLatitude\":0,\"startLongitude\":181,\"poiIds\":[1]}")]
    [InlineData("{\"startLatitude\":\"NaN\",\"startLongitude\":0,\"poiIds\":[1]}")]
    [InlineData("{\"startLatitude\":0,\"startLongitude\":\"Infinity\",\"poiIds\":[1]}")]
    [InlineData("{\"startLatitude\":1e309,\"startLongitude\":0,\"poiIds\":[1]}")]
    public async Task Missing_invalid_or_duplicate_route_input_is_400(string json)
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/routes/preview", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.RoutingHttp.Requests);
    }

    [Fact]
    public async Task Hidden_or_missing_pois_reject_entire_preview_and_report_requested_ids()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var visible = await fixture.AddPoiAsync();
        var hidden = await fixture.AddPoiAsync(status: PoiPublicationStatus.Hidden);
        using var response = await fixture.Client.PostAsJsonAsync("/api/routes/preview", new
        {
            startLatitude = 0, startLongitude = 0, poiIds = new[] { visible, hidden, int.MaxValue }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { hidden, int.MaxValue }, json.RootElement.GetProperty("unavailablePoiIds")
            .EnumerateArray().Select(value => value.GetInt32()));
        Assert.Empty(fixture.Factory.RoutingHttp.Requests);
    }

    [Fact]
    public async Task Preview_moves_from_previous_stop_sums_legs_and_never_writes_route_records()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var far = await fixture.AddPoiAsync("East", longitude: 2);
        var near = await fixture.AddPoiAsync("Near", longitude: 1);
        var middle = await fixture.AddPoiAsync("West", longitude: -1.1);
        using var response = await fixture.Client.PostAsJsonAsync("/api/routes/preview", new
        {
            startLatitude = 0, startLongitude = 0, poiIds = new[] { far, middle, near }
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = await response.Content.ReadFromJsonAsync<RoutePreviewDto>();
        // Sau điểm 1 phải chọn điểm 2, dù điểm -1.1 gần điểm xuất phát hơn điểm 2.
        Assert.Equal(new[] { near, far, middle }, preview!.Stops.Select(stop => stop.PoiId));
        Assert.Equal(new[] { 1, 2, 3 }, preview.Stops.Select(stop => stop.Order));
        Assert.Equal(2000, preview.Stops[1].DistanceFromPreviousMeters);
        Assert.Equal(preview.Stops.Sum(stop => stop.DistanceFromPreviousMeters), preview.TotalDistanceMeters, 6);
        Assert.Equal(6000, preview.TotalDistanceMeters);
        Assert.Equal(3000, preview.TotalDurationSeconds);
        Assert.Equal(preview.Stops.Sum(stop => stop.DurationFromPreviousSeconds), preview.TotalDurationSeconds);
        Assert.Equal("nearestNeighbor", preview.Algorithm);
        Assert.Equal("walking", preview.DistanceType);
        Assert.Equal("foot-walking", preview.Profile);
        Assert.Equal("openrouteservice", preview.Provider);
        Assert.Equal("LineString", preview.Geometry.Type);
        Assert.Equal(4, preview.Geometry.Coordinates.Count);
        var sent = Assert.Single(fixture.Factory.RoutingHttp.Requests);
        using var sentJson = JsonDocument.Parse(sent.Body);
        Assert.Equal(new[] { 0d, 0d }, sentJson.RootElement.GetProperty("coordinates")[0]
            .EnumerateArray().Select(value => value.GetDouble()));
        Assert.Equal(new[] { 1d, 0d }, sentJson.RootElement.GetProperty("coordinates")[1]
            .EnumerateArray().Select(value => value.GetDouble()));

        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(3, await db.PointsOfInterest.CountAsync());
        Assert.Equal(0, await db.Routes.CountAsync());
        Assert.Equal(0, await db.UsageEvents.CountAsync());
        Assert.Equal(0, await db.SystemLogs.CountAsync());
    }

    [Fact]
    public async Task Equidistant_choices_use_lowest_id_and_do_not_repeat_stops()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var first = await fixture.AddPoiAsync(longitude: -1);
        var second = await fixture.AddPoiAsync(longitude: 1);
        using var response = await fixture.Client.PostAsJsonAsync("/api/routes/preview", new
        {
            startLatitude = 0, startLongitude = 0, poiIds = new[] { second, first }
        });
        response.EnsureSuccessStatusCode();
        var preview = await response.Content.ReadFromJsonAsync<RoutePreviewDto>();
        Assert.Equal(new[] { first, second }, preview!.Stops.Select(stop => stop.PoiId));
        Assert.Equal(2, preview.Stops.Select(stop => stop.PoiId).Distinct().Count());
    }

    [Fact]
    public async Task Single_poi_at_start_has_zero_total_distance()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var id = await fixture.AddPoiAsync(latitude: 10, longitude: 106);
        using var response = await fixture.Client.PostAsJsonAsync("/api/routes/preview", new
        {
            startLatitude = 10, startLongitude = 106, poiIds = new[] { id }
        });
        response.EnsureSuccessStatusCode();
        var preview = await response.Content.ReadFromJsonAsync<RoutePreviewDto>();
        Assert.Single(preview!.Stops);
        Assert.Equal(0, preview.TotalDistanceMeters);
        Assert.Equal(0, preview.TotalDurationSeconds);
        Assert.Single(fixture.Factory.RoutingHttp.Requests);
    }
}
