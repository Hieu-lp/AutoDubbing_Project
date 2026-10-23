using System.Net;
using System.Net.Http.Json;
using AutoDubbing.Data.Entities;
using AutoDubbing.Service.Geography;
using AutoDubbing.Service.Pois;
using Xunit;

namespace AutoDubbing.Tests;

public sealed class PoiReadTests
{
    [Fact]
    public async Task Empty_database_returns_200_and_empty_arrays_without_authentication()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        using var list = await fixture.Client.GetAsync("/api/pois");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Empty((await list.Content.ReadFromJsonAsync<PoiSummaryDto[]>())!);
        Assert.Empty((await fixture.Client.GetFromJsonAsync<NearbyPoiDto[]>(
            "/api/pois/nearby?latitude=0&longitude=0&radiusMeters=1000"))!);
    }

    [Fact]
    public async Task List_and_detail_expose_only_published_pois_and_public_dtos()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var first = await fixture.AddPoiAsync("First");
        await fixture.AddPoiAsync(status: PoiPublicationStatus.Hidden);
        await fixture.AddPoiAsync(status: "Draft");
        await fixture.AddPoiAsync(status: "published");
        var last = await fixture.AddPoiAsync("Last");
        using var response = await fixture.Client.GetAsync("/api/pois");
        var pois = await response.Content.ReadFromJsonAsync<PoiSummaryDto[]>();
        Assert.NotNull(pois);
        Assert.Equal(new[] { first, last }, pois.Select(poi => poi.Id));
        Assert.Null(pois[0].ThumbnailUrl);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("userId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        var detail = await fixture.Client.GetFromJsonAsync<PoiDetailDto>($"/api/pois/{first}");
        Assert.Equal("Nội dung gốc", detail!.OriginalDescription);
        Assert.Equal(50, detail.ProximityRadius);
    }

    [Fact]
    public async Task Missing_and_hidden_details_are_404()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var hidden = await fixture.AddPoiAsync(status: PoiPublicationStatus.Hidden);
        foreach (var id in new[] { hidden, int.MaxValue, 0, -1 })
        {
            using var response = await fixture.Client.GetAsync($"/api/pois/{id}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("?latitude=0")]
    [InlineData("?latitude=0&longitude=0")]
    [InlineData("?longitude=0&radiusMeters=10")]
    [InlineData("?latitude=NaN&longitude=0&radiusMeters=10")]
    [InlineData("?latitude=Infinity&longitude=0&radiusMeters=10")]
    [InlineData("?latitude=-Infinity&longitude=0&radiusMeters=10")]
    [InlineData("?latitude=91&longitude=0&radiusMeters=10")]
    [InlineData("?latitude=-91&longitude=0&radiusMeters=10")]
    [InlineData("?latitude=0&longitude=181&radiusMeters=10")]
    [InlineData("?latitude=0&longitude=-181&radiusMeters=10")]
    [InlineData("?latitude=0&longitude=NaN&radiusMeters=10")]
    [InlineData("?latitude=0&longitude=Infinity&radiusMeters=10")]
    [InlineData("?latitude=0&longitude=0&radiusMeters=0")]
    [InlineData("?latitude=0&longitude=0&radiusMeters=-1")]
    [InlineData("?latitude=0&longitude=0&radiusMeters=NaN")]
    [InlineData("?latitude=0&longitude=0&radiusMeters=Infinity")]
    [InlineData("?latitude=not-a-number&longitude=0&radiusMeters=10")]
    public async Task Invalid_or_missing_nearby_parameters_are_400(string query)
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        using var response = await client.GetAsync("/api/pois/nearby" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("errors", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Nearby_uses_search_radius_and_orders_distance_then_id()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var west = await fixture.AddPoiAsync("West", longitude: -0.005, proximityRadius: 1);
        var east = await fixture.AddPoiAsync("East", longitude: 0.005, proximityRadius: 1);
        var origin = await fixture.AddPoiAsync("Origin");
        await fixture.AddPoiAsync("Far", longitude: 1, proximityRadius: 1_000_000);
        await fixture.AddPoiAsync("Hidden", status: PoiPublicationStatus.Hidden);
        var nearby = await fixture.Client.GetFromJsonAsync<NearbyPoiDto[]>(
            "/api/pois/nearby?latitude=0&longitude=0&radiusMeters=1000");
        Assert.NotNull(nearby);
        Assert.Equal(new[] { origin, west, east }, nearby.Select(poi => poi.Id));
        Assert.Equal(0, nearby[0].DistanceMeters);
        Assert.InRange(nearby[1].DistanceMeters, 555, 557);
        Assert.Equal(nearby[1].DistanceMeters, nearby[2].DistanceMeters);
    }

    [Fact]
    public async Task Nearby_includes_exact_radius_boundary_and_excludes_point_just_outside()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        var boundaryId = await fixture.AddPoiAsync(longitude: 1);
        var radius = GeoMath.DistanceMeters(0, 0, 0, 1);
        var atBoundary = await fixture.Client.GetFromJsonAsync<NearbyPoiDto[]>(FormattableString.Invariant(
            $"/api/pois/nearby?latitude=0&longitude=0&radiusMeters={radius:R}"));
        Assert.Equal(boundaryId, Assert.Single(atBoundary!).Id);
        radius = Math.BitDecrement(radius);
        Assert.Empty((await fixture.Client.GetFromJsonAsync<NearbyPoiDto[]>(FormattableString.Invariant(
            $"/api/pois/nearby?latitude=0&longitude=0&radiusMeters={radius:R}")))!);
    }
}
