using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AutoDubbing.Service.Authentication;
using AutoDubbing.Service.Pois;
using Xunit;

namespace AutoDubbing.Tests;

public sealed class AdminPoiApiTests
{
    [Fact]
    public async Task Poi_management_requires_an_authenticated_admin()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();

        using var list = await fixture.Client.GetAsync("/api/admin/pois");
        using var create = await fixture.Client.PostAsJsonAsync("/api/admin/pois", ValidPoi());

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_list_update_and_delete_a_poi()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        fixture.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await LoginAsync(fixture.Client, fixture.Factory.Password));

        using var create = await fixture.Client.PostAsJsonAsync("/api/admin/pois", ValidPoi());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(create.Headers.Location);
        var created = await create.Content.ReadFromJsonAsync<PoiManagementResponse>();
        Assert.NotNull(created);
        Assert.Equal("Địa điểm kiểm thử", created.Name);
        Assert.Equal("Hidden", created.Status);

        using var list = await fixture.Client.GetAsync("/api/admin/pois");
        var pois = await list.Content.ReadFromJsonAsync<PoiManagementResponse[]>();
        Assert.Equal(new[] { created.Id }, pois!.Select(poi => poi.Id));

        using var update = await fixture.Client.PutAsJsonAsync($"/api/admin/pois/{created.Id}",
            ValidPoi() with { Name = "Địa điểm đã cập nhật", Status = "Published" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<PoiManagementResponse>();
        Assert.Equal("Địa điểm đã cập nhật", updated!.Name);
        Assert.Equal("Published", updated.Status);

        using var anonymousList = await fixture.Client.GetAsync("/api/pois");
        var publicPois = await anonymousList.Content.ReadFromJsonAsync<AutoDubbing.Service.Pois.PoiSummaryDto[]>();
        Assert.Contains(publicPois!, poi => poi.Id == created.Id);

        using var delete = await fixture.Client.DeleteAsync($"/api/admin/pois/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        using var missing = await fixture.Client.GetAsync($"/api/admin/pois/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

    }

    [Fact]
    public async Task Invalid_poi_data_returns_validation_problem_details()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        fixture.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await LoginAsync(fixture.Client, fixture.Factory.Password));

        using var response = await fixture.Client.PostAsJsonAsync("/api/admin/pois",
            ValidPoi() with { Latitude = 91 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("errors", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Poi_with_translation_cannot_be_deleted()
    {
        await using var fixture = await DemoApiFixture.CreateAsync();
        fixture.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await LoginAsync(fixture.Client, fixture.Factory.Password));
        var id = await fixture.AddPoiAsync();
        await fixture.AddTranslationAsync(id, "en", "Translated description");

        using var response = await fixture.Client.DeleteAsync($"/api/admin/pois/{id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("bản dịch", await response.Content.ReadAsStringAsync());
    }

    private static PoiManagementRequest ValidPoi() => new()
    {
        Name = "Địa điểm kiểm thử",
        Latitude = 10.7769,
        Longitude = 106.7009,
        ProximityRadius = 50,
        OriginalDescription = "Mô tả kiểm thử",
        OriginalLanguage = "vi",
        Status = "Hidden"
    };

    private static async Task<string> LoginAsync(HttpClient client, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/admin/auth/login",
            new { Username = "Admin", Password = password });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<AdminLoginResponse>();
        return login!.AccessToken;
    }
}
