using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AutoDubbing.API.Controllers;
using AutoDubbing.Data.Contexts;
using AutoDubbing.Data.Entities;
using AutoDubbing.Service.Authentication;
using AutoDubbing.Service.ContentPreparation;
using AutoDubbing.Service.Monitoring;
using AutoDubbing.Service.Pois;
using AutoDubbing.Service.Routes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoDubbing.Tests;

public sealed class AdminIntegrationTests
{
    [LocalSqlFact]
    public async Task Real_local_sql_keeps_admin_route_content_events_and_logs_across_api_restarts()
    {
        var connection = Environment.GetEnvironmentVariable("AUTODUBBING_SQL_TEST_CONNECTION")!;
        string password; string signingKey; int poiId; int routeId;
        await using (var first = new AdminApiFactory { SqlServerTestConnection = connection, GenerationConfigured = true, RoutingApiKey = "fixture-ors-key" })
        {
            await using (var checkScope = first.Services.CreateAsyncScope())
                Assert.Equal(0, await checkScope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync());
            using var admin = await Admin(first); password = first.Password; signingKey = first.SigningKey;
            poiId = await Poi(admin);
            (await admin.PostAsJsonAsync($"/api/admin/pois/{poiId}/generate-content", new { languages = new[] { "en" } })).EnsureSuccessStatusCode();
            using var created = await admin.PostAsJsonAsync("/api/admin/routes", new { name = "SQL integration fixture", poiIds = new[] { poiId }, status = "Published" });
            created.EnsureSuccessStatusCode(); routeId = (await created.Content.ReadFromJsonAsync<SavedRouteDto>())!.Id;
            var saved = (await admin.GetFromJsonAsync<PoiContentDto>($"/api/pois/{poiId}/content?language=en"))!;
            (await admin.PostAsJsonAsync("/api/usage-events", new { eventId = Guid.NewGuid(), guestId = Guid.NewGuid(), eventType = "manual_play", poiId, language = "en", audioId = saved.Audio!.Id })).EnsureSuccessStatusCode();
            await using var scope = first.Services.CreateAsyncScope();
            var writer = scope.ServiceProvider.GetRequiredService<SystemEventWriter>();
            await writer.WriteJobAsync(new PreparationJob(Guid.NewGuid(), poiId, "en", "fixture", "Failed", true,
                "SupabaseTimeout", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, true, "upload"), default);
        }
        await using var restarted = new AdminApiFactory(signingKey) { SqlServerTestConnection = connection };
        using var client = restarted.CreateHttpsClient();
        using var login = await client.PostAsJsonAsync("/api/admin/auth/login", new { username = "Admin", password });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<AdminLoginResponse>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(routeId, Assert.Single((await client.GetFromJsonAsync<SavedRouteDto[]>("/api/routes"))!).Id);
        var content = (await client.GetFromJsonAsync<PoiContentDto>($"/api/pois/{poiId}/content?language=en"))!;
        Assert.False(content.IsFallback); Assert.NotNull(content.Audio);
        var summary = (await client.GetFromJsonAsync<AdminDashboardSummary>("/api/admin/dashboard/summary"))!;
        Assert.Equal(1, summary.Usage.ManualPlayCount); Assert.Equal(0, summary.Usage.ActiveUsers);
        Assert.Equal("SupabaseTimeout", Assert.Single(summary.Pipeline.RecentErrors).ErrorCode);
        Assert.Equal(0, restarted.GenerationHttp.TranslationCalls + restarted.GenerationHttp.AudioCalls + restarted.GenerationHttp.UploadCalls);
    }
    private static async Task<HttpClient> Admin(AdminApiFactory factory)
    {
        await factory.SeedAdminAsync();
        var client = factory.CreateHttpsClient();
        using var login = await client.PostAsJsonAsync("/api/admin/auth/login", new { username = "Admin", password = factory.Password });
        login.EnsureSuccessStatusCode();
        var result = (await login.Content.ReadFromJsonAsync<AdminLoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
        return client;
    }
    private static object PoiInput(string status = "Published", string source = "Nội dung gốc.") => new {
        name = "Fixture POI", latitude = 10.77, longitude = 106.69, proximityRadius = 50,
        originalDescription = source, originalLanguage = "vi", status
    };
    private static async Task<int> Poi(HttpClient client, string status = "Published")
    {
        using var response = await client.PostAsJsonAsync("/api/admin/pois", PoiInput(status));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<PoiManagementResponse>())!.Id;
    }

    [Fact]
    public async Task Management_requires_active_admin_and_model_has_seven_entities_without_tour_session()
    {
        await using var factory = new AdminApiFactory();
        using var admin = await Admin(factory); using var guest = factory.CreateHttpsClient();
        foreach (var endpoint in new[] { "users", "routes", "usage-events", "logs", "pois/1/contents", "pois/1/content?language=vi" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await guest.GetAsync("/api/admin/" + endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsJsonAsync("/api/admin/pois/1/prepare-content", new { language = "en" })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(7, db.Model.GetEntityTypes().Count());
        Assert.DoesNotContain(db.Model.GetEntityTypes(), entity => entity.GetTableName() == "SESSION");
        Assert.Null(db.Model.FindEntityType(typeof(TourRoute))!.FindProperty("SessionId"));
        Assert.All(db.Model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys()), fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
    }

    [Fact]
    public async Task Accounts_preserve_last_active_admin_update_profile_and_never_expose_passwords()
    {
        await using var factory = new AdminApiFactory(); using var client = await Admin(factory);
        var original = new { username = "Admin", fullName = "Tên mới", status = "Locked" };
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/admin/users/1", original)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/users", new { username = "two", fullName = "Second", password = "short", status = "Active" })).StatusCode);
        using var create = await client.PostAsJsonAsync("/api/admin/users", new { username = "two", fullName = "Second", password = factory.Password, status = "Active" });
        create.EnsureSuccessStatusCode(); var second = (await create.Content.ReadFromJsonAsync<AdminProfile>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/admin/users", new { username = "two", fullName = "Duplicate", password = factory.Password, status = "Active" })).StatusCode);
        (await client.PutAsJsonAsync($"/api/admin/users/{second.Id}", new { username = "two", fullName = "Second", status = "Locked" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/admin/users/1", original)).StatusCode);
        (await client.PutAsJsonAsync("/api/admin/users/1", new { username = "Admin", fullName = "Tên mới", status = "Active" })).EnsureSuccessStatusCode();
        Assert.Equal("Tên mới", (await client.GetFromJsonAsync<AdminProfile>("/api/admin/auth/me"))!.FullName);
        var json = await client.GetStringAsync("/api/admin/users");
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain(factory.Password, json);
        using var guest = factory.CreateHttpsClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await guest.PostAsJsonAsync("/api/admin/auth/login", new { username = "two", password = factory.Password })).StatusCode);
    }

    [Fact]
    public async Task Published_routes_keep_admin_order_follow_edits_hide_invalid_pois_and_soft_delete_without_losing_history()
    {
        await using var factory = new AdminApiFactory { RoutingApiKey = "fixture-ors-key" }; using var admin = await Admin(factory);
        using var guest = factory.CreateHttpsClient(); var first = await Poi(admin); var second = await Poi(admin); var hidden = await Poi(admin, "Hidden");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/routes", new { name = "Bad", poiIds = new[] { first, first }, status = "Published" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/routes", new { name = "Bad", poiIds = new[] { first, hidden }, status = "Published" })).StatusCode);
        using var create = await admin.PostAsJsonAsync("/api/admin/routes", new { name = "Ordered", poiIds = new[] { second, first }, status = "Published" });
        create.EnsureSuccessStatusCode(); var route = (await create.Content.ReadFromJsonAsync<SavedRouteDto>())!;
        Assert.Equal(new[] { second, first }, Assert.Single((await guest.GetFromJsonAsync<SavedRouteDto[]>("/api/routes"))!).PoiIds);
        using var preview = await guest.PostAsJsonAsync($"/api/routes/{route.Id}/preview", new { startLatitude = 10.76, startLongitude = 106.68 });
        preview.EnsureSuccessStatusCode(); var path = (await preview.Content.ReadFromJsonAsync<RoutePreviewDto>())!;
        Assert.Equal("adminOrder", path.Algorithm); Assert.Equal(new[] { second, first }, path.Stops.Select(stop => stop.PoiId));
        (await guest.PostAsJsonAsync("/api/usage-events", new { eventId = Guid.NewGuid(), guestId = Guid.NewGuid(), eventType = "route_selection", routeId = route.Id })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/admin/routes/{route.Id}", new { name = "Renamed", poiIds = new[] { first, second }, status = "Published" })).EnsureSuccessStatusCode();
        var updated = Assert.Single((await guest.GetFromJsonAsync<SavedRouteDto[]>("/api/routes"))!);
        Assert.Equal("Renamed", updated.Name); Assert.NotEqual(route.Version, updated.Version); Assert.Equal(new[] { first, second }, updated.PoiIds);
        (await admin.PutAsJsonAsync($"/api/admin/pois/{second}", PoiInput("Hidden"))).EnsureSuccessStatusCode();
        Assert.Empty((await guest.GetFromJsonAsync<SavedRouteDto[]>("/api/routes"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.PostAsJsonAsync($"/api/routes/{route.Id}/preview", new { startLatitude = 10.76, startLongitude = 106.68 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/admin/pois/{first}")).StatusCode);
        (await admin.DeleteAsync($"/api/admin/routes/{route.Id}")).EnsureSuccessStatusCode();
        Assert.Empty((await admin.GetFromJsonAsync<SavedRouteDto[]>("/api/admin/routes"))!);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("Deleted", (await db.Routes.SingleAsync()).Status); Assert.Single(await db.UsageEvents.ToArrayAsync());
    }

    [Fact]
    public async Task Playback_events_are_idempotent_validate_actual_audio_and_populate_sql_dashboard()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true }; using var admin = await Admin(factory);
        using var guest = factory.CreateHttpsClient(); var id = await Poi(admin);
        (await admin.PostAsJsonAsync($"/api/admin/pois/{id}/generate-content", new { languages = new[] { "en" } })).EnsureSuccessStatusCode();
        var content = (await guest.GetFromJsonAsync<PoiContentDto>($"/api/pois/{id}/content?language=en"))!;
        var visitor = Guid.NewGuid(); var eventId = Guid.NewGuid();
        var input = new { eventId, guestId = visitor, eventType = "manual_play", poiId = id, language = "en", audioId = content.Audio!.Id };
        (await guest.PostAsJsonAsync("/api/usage-events", input)).EnsureSuccessStatusCode();
        (await guest.PostAsJsonAsync("/api/usage-events", input)).EnsureSuccessStatusCode();
        (await guest.PostAsJsonAsync("/api/usage-events", new { eventId = Guid.NewGuid(), guestId = visitor, eventType = "auto_play", poiId = id, language = "en", audioId = content.Audio.Id })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await guest.PostAsJsonAsync("/api/usage-events", new { eventId = Guid.NewGuid(), guestId = visitor, eventType = "manual_play", poiId = id, language = "vi", audioId = content.Audio.Id })).StatusCode);
        for (var i = 0; i < 3; i++) (await guest.PostAsJsonAsync("/api/visitor-heartbeat", new { guestId = Guid.NewGuid() })).EnsureSuccessStatusCode();
        (await guest.PostAsJsonAsync("/api/visitor-heartbeat", new { guestId = visitor })).EnsureSuccessStatusCode();
        var summary = (await admin.GetFromJsonAsync<AdminDashboardSummary>("/api/admin/dashboard/summary"))!;
        Assert.Equal(4, summary.Usage.ActiveUsers); Assert.Equal(1, summary.Usage.AutoPlayCount); Assert.Equal(1, summary.Usage.ManualPlayCount);
        Assert.Equal(2, Assert.Single(summary.Usage.ListensByPoi).Count); Assert.Equal("en", Assert.Single(summary.Usage.ListensByLanguage).Language);
        Assert.Equal(2, (await admin.GetFromJsonAsync<JsonElement[]>("/api/admin/usage-events?language=en"))!.Length);
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement[]>("/api/admin/usage-events?language=ja"))!);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.UsageEvents.CountAsync());
        Assert.Equal((await db.PointsOfInterest.SingleAsync()).SourceVersion, (await db.Audios.SingleAsync()).SourceVersion);
    }

    [Fact]
    public async Task Source_edits_invalidate_both_translations_and_audio_and_hidden_content_stays_admin_only()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true }; using var admin = await Admin(factory);
        using var guest = factory.CreateHttpsClient(); var id = await Poi(admin);
        (await admin.PostAsJsonAsync($"/api/admin/pois/{id}/generate-content", new { languages = new[] { "en" } })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/admin/pois/{id}", PoiInput("Published", "Nội dung gốc đã cập nhật."))).EnsureSuccessStatusCode();
        var changed = (await guest.GetFromJsonAsync<PoiContentDto>($"/api/pois/{id}/content?language=en"))!;
        Assert.True(changed.IsFallback); Assert.Null(changed.Audio); Assert.Equal("Nội dung gốc đã cập nhật.", changed.Text);
        (await admin.PutAsJsonAsync($"/api/admin/pois/{id}", PoiInput("Hidden", "Nội dung gốc đã cập nhật."))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/pois/{id}/content?language=vi")).StatusCode);
        (await admin.GetAsync($"/api/admin/pois/{id}/content?language=vi")).EnsureSuccessStatusCode();
        var inventory = await admin.GetStringAsync($"/api/admin/pois/{id}/contents"); Assert.Contains("Stale", inventory);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.Translations.ToArrayAsync()); Assert.Single(await db.Audios.ToArrayAsync());
        Assert.Equal("Stale", (await db.Audios.SingleAsync()).Status);
    }

    [Fact]
    public async Task Background_failures_persist_sanitized_sql_logs_with_poi_language_job_and_stage()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true }; using var admin = await Admin(factory);
        var id = await Poi(admin); factory.GenerationHttp.NextAudioError = HttpStatusCode.Forbidden;
        using var response = await admin.PostAsJsonAsync($"/api/admin/pois/{id}/prepare-content", new { language = "en" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var queued = (await response.Content.ReadFromJsonAsync<PreparationItemResponse>())!;
        JsonElement[] logs = [];
        for (var i = 0; i < 150; i++)
        {
            using var logResponse = await admin.GetAsync($"/api/admin/logs?poiId={id}&language=en&level=error");
            if (logResponse.IsSuccessStatusCode)
                logs = (await logResponse.Content.ReadFromJsonAsync<JsonElement[]>()) ?? [];
            else if (logResponse.StatusCode != HttpStatusCode.InternalServerError)
                logResponse.EnsureSuccessStatusCode();
            if (logs.Length > 0) break; await Task.Delay(20);
        }
        var error = Assert.Single(logs); Assert.Equal(queued.JobId, error.GetProperty("jobId").GetGuid());
        Assert.Equal("tts", error.GetProperty("stage").GetString()); Assert.Equal("GeminiForbidden", error.GetProperty("errorCode").GetString());
        Assert.DoesNotContain("fixture-gemini-key", error.GetRawText()); Assert.DoesNotContain(factory.Password, error.GetRawText());
        AdminDashboardSummary? summary = null;
        for (var i = 0; i < 50; i++)
        {
            using var summaryResponse = await admin.GetAsync("/api/admin/dashboard/summary");
            if (summaryResponse.IsSuccessStatusCode)
            {
                summary = await summaryResponse.Content.ReadFromJsonAsync<AdminDashboardSummary>();
                if (summary?.Pipeline.RecentErrors.Count > 0) break;
            }
            else if (summaryResponse.StatusCode != HttpStatusCode.InternalServerError)
                summaryResponse.EnsureSuccessStatusCode();
            await Task.Delay(20);
        }
        Assert.NotNull(summary);
        Assert.Equal(id, Assert.Single(summary.Pipeline.RecentErrors).PoiId);
    }

    [Fact]
    public void Active_visitors_are_unique_expire_and_are_not_reconstructed_from_request_counts()
    {
        var clock = new PresenceClock(); var presence = new VisitorPresence(clock); var first = Guid.NewGuid();
        presence.Touch(first); presence.Touch(first); Assert.Equal(1, presence.Count);
        presence.Touch(Guid.NewGuid()); Assert.Equal(2, presence.Count);
        clock.Now = clock.Now.AddSeconds(59); Assert.Equal(2, presence.Count);
        presence.Touch(first);
        clock.Now = clock.Now.AddSeconds(1); Assert.Equal(1, presence.Count);
        clock.Now = clock.Now.AddSeconds(59); Assert.Equal(0, presence.Count);
        Assert.Equal(0, new VisitorPresence(clock).Count);
    }
    private sealed class PresenceClock : TimeProvider
    { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
}

public sealed class LocalSqlFactAttribute : FactAttribute
{
    public LocalSqlFactAttribute()
    { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTODUBBING_SQL_TEST_CONNECTION"))) Skip = "Requires explicit isolated local SQL test database."; }
}
