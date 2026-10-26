using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AutoDubbing.Data.Contexts;
using AutoDubbing.API.Controllers;
using System.Net.Http.Headers;
using AutoDubbing.Data.Entities;
using AutoDubbing.Service.ContentGeneration;
using AutoDubbing.Service.ContentPreparation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoDubbing.Tests;

public sealed class ContentPreparationApiTests
{
    private static async Task<HttpClient> DashboardAdmin(AdminApiFactory factory)
    {
        await factory.SeedAdminAsync();
        var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync("/api/admin/auth/login", new { username = "Admin", password = factory.Password });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task Dashboard_requires_active_admin_and_empty_history_is_not_a_pipeline_failure()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true, StartPreparationWorker = false };
        using (var anonymous = factory.CreateHttpsClient())
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/dashboard/summary")).StatusCode);
        using var client = await DashboardAdmin(factory);
        var result = (await client.GetFromJsonAsync<AdminDashboardSummary>("/api/admin/dashboard/summary"))!;
        Assert.Equal("online", result.Health.Pipeline);
        Assert.False(result.Pipeline.IsBlocked);
        Assert.Equal(0, result.Pipeline.Failed);
        Assert.Empty(result.Pipeline.FailedJobs);
        Assert.Empty(result.Pipeline.RecentErrors);
        Assert.True(result.Pipeline.LogsAvailable);
        Assert.True(result.Pipeline.History.LostOnRestart);
        Assert.Equal(30, result.Pipeline.History.RetentionMinutes);
        await factory.ChangeStatusAsync(1, "Locked");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/dashboard/summary")).StatusCode);
        Assert.Equal(0, factory.GenerationHttp.TranslationCalls + factory.GenerationHttp.AudioCalls + factory.GenerationHttp.UploadCalls);
    }

    [Theory]
    [InlineData(false, true, "GenerationNotConfigured")]
    [InlineData(true, false, "GenerationPaused")]
    public async Task Dashboard_reports_missing_configuration_or_disabled_generation_without_provider_calls(bool configured, bool enabled, string code)
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = configured, PreparationEnabled = enabled, StartPreparationWorker = false };
        using var client = await DashboardAdmin(factory);
        var result = (await client.GetFromJsonAsync<AdminDashboardSummary>("/api/admin/dashboard/summary"))!;
        Assert.Equal("warning", result.Health.Pipeline);
        Assert.True(result.Pipeline.IsBlocked);
        Assert.Contains(result.Pipeline.Reasons, reason => reason.Code == code && reason.RequiresOperatorAction);
        Assert.Empty(result.Pipeline.FailedJobs);
        Assert.Equal(0, factory.GenerationHttp.TranslationCalls + factory.GenerationHttp.AudioCalls + factory.GenerationHttp.UploadCalls);
    }

    [Fact]
    public async Task Dashboard_failure_details_match_counter_even_with_empty_SQL_logs_and_later_success_keeps_history()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true, StartPreparationWorker = false };
        using var client = await DashboardAdmin(factory);
        var poiId = await AddPoi(factory);
        var queue = factory.Services.GetRequiredService<IContentPreparationQueue>();
        var first = queue.Enqueue(poiId, "zh-hans", "a", true, true).Job!;
        await queue.TakeAsync(default);
        queue.Complete(first.Id, "Failed", true, "SupabaseObjectMismatch", failureStage: "upload");
        var result = (await client.GetFromJsonAsync<AdminDashboardSummary>("/api/admin/dashboard/summary"))!;
        var failure = Assert.Single(result.Pipeline.FailedJobs);
        Assert.Equal(result.Pipeline.Failed, result.Pipeline.FailedJobs.Count);
        Assert.Equal(first.Id, failure.JobId);
        Assert.Equal(poiId, failure.PoiId);
        Assert.Equal("Fixture", failure.PoiName);
        Assert.Equal("zh-hans", failure.Language);
        Assert.Equal("upload", failure.Stage);
        Assert.NotNull(failure.FailedAtUtc);
        Assert.Equal("SupabaseObjectMismatch", failure.ErrorCode);
        Assert.Empty(result.Pipeline.RecentErrors);
        Assert.False(result.Pipeline.IsBlocked);
        Assert.Equal("online", result.Health.Pipeline);
        Assert.Null(failure.RecoveredAtUtc);
        var retry = queue.Enqueue(poiId, "zh-hans", "a", true, true).Job!;
        await queue.TakeAsync(default);
        queue.Complete(retry.Id, "Ready", true);
        result = (await client.GetFromJsonAsync<AdminDashboardSummary>("/api/admin/dashboard/summary"))!;
        Assert.Equal(1, result.Pipeline.Failed);
        Assert.NotNull(Assert.Single(result.Pipeline.FailedJobs).RecoveredAtUtc);
        Assert.False(result.Pipeline.IsBlocked);
        Assert.Equal("online", result.Health.Pipeline);
        Assert.Equal(0, factory.GenerationHttp.TranslationCalls + factory.GenerationHttp.AudioCalls + factory.GenerationHttp.UploadCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dashboard_quota_block_is_distinct_from_history_and_infinite_wait_is_not_a_retry_date(bool needsOperator)
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true, StartPreparationWorker = false };
        using var client = await DashboardAdmin(factory);
        var queue = factory.Services.GetRequiredService<IContentPreparationQueue>();
        var code = needsOperator ? "QuotaUnavailable" : "DailyQuotaExceeded";
        var until = needsOperator ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.AddMinutes(30);
        var job = queue.Enqueue(1, "en", "a", false, true).Job!;
        await queue.TakeAsync(default);
        queue.Complete(job.Id, "QuotaExceeded", true, code, until, "tts");
        queue.Block(code, until);
        var result = (await client.GetFromJsonAsync<AdminDashboardSummary>("/api/admin/dashboard/summary"))!;
        Assert.True(result.Pipeline.IsBlocked);
        Assert.Equal("warning", result.Health.Pipeline);
        var reason = Assert.Single(result.Pipeline.Reasons, item => item.Code == code);
        Assert.Equal(needsOperator, reason.RequiresOperatorAction);
        Assert.Equal(needsOperator ? null : (DateTimeOffset?)until, reason.RetryAtUtc);
        Assert.Equal(1, result.Pipeline.Failed);
        Assert.Equal(code, Assert.Single(result.Pipeline.FailedJobs).ErrorCode);
        Assert.Equal(0, factory.GenerationHttp.AudioCalls);
    }

    [Fact]
    public async Task Dashboard_does_not_return_unclassified_errors_raw_SQL_logs_secrets_or_stacks()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true, StartPreparationWorker = false };
        using var client = await DashboardAdmin(factory);
        var queue = factory.Services.GetRequiredService<IContentPreparationQueue>();
        var job = queue.Enqueue(1, "en", "a", false, true).Job!;
        await queue.TakeAsync(default);
        queue.Complete(job.Id, "Failed", false, "sb_secret_DO_NOT_EXPOSE", failureStage: "Password=DO_NOT_EXPOSE");
        queue.Block("Bearer DO_NOT_EXPOSE", DateTimeOffset.MaxValue);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SystemLogs.Add(new SystemLog { Component = "DO_NOT_EXPOSE", Message = "Server=secret;Password=DO_NOT_EXPOSE; stack trace", Level = "error", OccurredAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var json = await client.GetStringAsync("/api/admin/dashboard/summary");
        Assert.DoesNotContain("DO_NOT_EXPOSE", json);
        Assert.DoesNotContain(factory.SigningKey, json);
        Assert.DoesNotContain(client.DefaultRequestHeaders.Authorization!.Parameter!, json);
        Assert.DoesNotContain("fixture-gemini-key", json);
        Assert.DoesNotContain("sb_secret_fixture_key", json);
        Assert.DoesNotContain("stack trace", json);
        var result = JsonSerializer.Deserialize<AdminDashboardSummary>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.Equal("UnknownFailure", Assert.Single(result.Pipeline.FailedJobs).ErrorCode);
        Assert.Null(Assert.Single(result.Pipeline.FailedJobs).Stage);
        Assert.Single(result.Pipeline.RecentErrors);
        Assert.True(result.Pipeline.LogsAvailable);
    }

    private static async Task<int> AddPoi(AdminApiFactory factory, double latitude = 0, string status = "Published")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
        var poi = new PointOfInterest { UserId = 1, Name = "Fixture", OriginalLanguage = "vi",
            OriginalDescription = "Source description.", Latitude = latitude, Longitude = 0, Status = status, ProximityRadius = 73 };
        db.PointsOfInterest.Add(poi);
        await db.SaveChangesAsync();
        return poi.Id;
    }

    [Fact]
    public async Task Nearby_only_prepares_source_language_inside_500m_in_nearest_order_and_excludes_hidden()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true, StartPreparationWorker = false };
        await factory.SeedAdminAsync();
        var farther = await AddPoi(factory, 0.003);
        var nearest = await AddPoi(factory, 0.001);
        await AddPoi(factory, 0.006);
        await AddPoi(factory, 0, "Hidden");
        using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync("/api/pois/prepare-nearby", new { latitude = 0, longitude = 0 });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<NearbyPreparationResponse>())!;
        Assert.Equal(500, result.PreparationRadiusMeters);
        Assert.Equal(new[] { nearest, farther }, result.Items.Select(item => item.PoiId));
        Assert.All(result.Items, item => { Assert.Equal("vi", item.Language); Assert.True(item.TextReady); Assert.False(item.AudioReady); });
        var queue = factory.Services.GetRequiredService<IContentPreparationQueue>();
        Assert.Equal(nearest, (await queue.TakeAsync(default)).PoiId);
        Assert.Equal(farther, (await queue.TakeAsync(default)).PoiId);
        Assert.Equal(0, factory.GenerationHttp.TranslationCalls + factory.GenerationHttp.AudioCalls + factory.GenerationHttp.UploadCalls);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.All(await scope.ServiceProvider.GetRequiredService<AppDbContext>().PointsOfInterest.ToListAsync(), poi => Assert.Equal(73, poi.ProximityRadius));
    }

    [Fact]
    public async Task Ready_returns_immediately_even_when_generation_disabled_and_never_calls_provider()
    {
        await using var factory = new AdminApiFactory { PreparationEnabled = false };
        await factory.SeedAdminAsync();
        var id = await AddPoi(factory);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Audios.Add(new PoiAudio { PointOfInterestId = id, Language = "vi", Status = "Ready",
                FilePath = "https://fixture.test/existing.wav" });
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateHttpsClient();
        for (var i = 0; i < 2; i++)
        {
            using var response = await client.PostAsJsonAsync($"/api/pois/{id}/prepare-content", new { language = "vi" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var item = (await response.Content.ReadFromJsonAsync<PreparationItemResponse>())!;
            Assert.Equal("Ready", item.Status);
            Assert.True(item.AudioReady);
            Assert.Null(item.JobId);
        }
        using var missing = await client.PostAsJsonAsync($"/api/pois/{id}/prepare-content", new { language = "en" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, missing.StatusCode);
        var unavailable = (await missing.Content.ReadFromJsonAsync<PreparationItemResponse>())!;
        Assert.Equal("GenerationPaused", unavailable.ErrorCode);
        Assert.True(unavailable.Content!.IsFallback);
        Assert.False(unavailable.AudioReady);
        Assert.False(unavailable.TextReady);
        Assert.Equal(0, factory.GenerationHttp.TranslationCalls + factory.GenerationHttp.AudioCalls + factory.GenerationHttp.UploadCalls);
    }

    [Fact]
    public async Task Accepted_work_is_shared_pollable_and_source_change_supersedes_it_without_generation()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true, StartPreparationWorker = false };
        await factory.SeedAdminAsync();
        var id = await AddPoi(factory);
        using var client = factory.CreateHttpsClient();
        using var first = await client.PostAsJsonAsync($"/api/pois/{id}/prepare-content", new { language = "EN" });
        var job = (await first.Content.ReadFromJsonAsync<PreparationItemResponse>())!;
        using var again = await client.PostAsJsonAsync($"/api/pois/{id}/prepare-content", new { language = "en" });
        Assert.Equal(job.JobId, (await again.Content.ReadFromJsonAsync<PreparationItemResponse>())!.JobId);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(job.StatusUrl, first.Headers.Location!.OriginalString);
        var status = (await client.GetFromJsonAsync<PreparationItemResponse>(job.StatusUrl))!;
        Assert.Equal("Queued", status.Status);
        Assert.False(status.AudioReady);
        Assert.True(status.Content!.IsFallback);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.PointsOfInterest.Where(poi => poi.Id == id).ExecuteUpdateAsync(update => update.SetProperty(poi => poi.OriginalDescription, "Changed."));
        status = (await client.GetFromJsonAsync<PreparationItemResponse>(job.StatusUrl))!;
        Assert.Equal("Superseded", status.Status);
        var service = scope.ServiceProvider.GetRequiredService<IContentGenerationService>();
        var error = await Assert.ThrowsAsync<GenerationException>(() => service.GenerateAsync(id, ["en"], 1, default, job.SourceVersion));
        Assert.Equal("SourceChanged", error.Code);
        Assert.Equal(0, factory.GenerationHttp.TranslationCalls + factory.GenerationHttp.AudioCalls);
        await db.PointsOfInterest.Where(poi => poi.Id == id).ExecuteUpdateAsync(update => update.SetProperty(poi => poi.Status, "Hidden"));
        using var hiddenPoll = await client.GetAsync(job.StatusUrl);
        Assert.Equal(HttpStatusCode.NotFound, hiddenPoll.StatusCode);
        Assert.Null(await service.GenerateAsync(id, ["en"], 1, default, job.SourceVersion));
        Assert.Equal(0, factory.GenerationHttp.TranslationCalls + factory.GenerationHttp.AudioCalls);
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("en-us")]
    [InlineData(" en")]
    [InlineData("")]
    public async Task Invalid_language_is_400_and_does_not_generate(string language)
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true };
        using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync("/api/pois/1/prepare-content", new { language });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.GenerationHttp.TranslationCalls + factory.GenerationHttp.AudioCalls);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"latitude\":91,\"longitude\":0}")]
    [InlineData("{\"latitude\":0,\"longitude\":181}")]
    [InlineData("{\"latitude\":\"NaN\",\"longitude\":0}")]
    [InlineData("{\"latitude\":0,\"longitude\":\"Infinity\"}")]
    public async Task Missing_or_invalid_coordinates_are_400(string body)
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsync("/api/pois/prepare-nearby", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Hidden_is_404_and_POST_rate_limit_does_not_block_GET_polling()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true, StartPreparationWorker = false };
        await factory.SeedAdminAsync();
        var hidden = await AddPoi(factory, status: "Hidden");
        using var client = factory.CreateHttpsClient();
        for (var i = 0; i < 8; i++)
        {
            using var response = await client.PostAsJsonAsync($"/api/pois/{hidden}/prepare-content", new { language = "vi" });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var limited = await client.PostAsJsonAsync("/api/pois/prepare-nearby", new { latitude = 0, longitude = 0 });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        using var read = await client.GetAsync("/api/content-preparations/settings");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(0, factory.GenerationHttp.AudioCalls);
    }

    [Fact]
    public async Task OpenApi_contains_new_anonymous_preparation_and_read_only_status_contracts()
    {
        await using var factory = new AdminApiFactory { ExposeOpenApi = true };
        using var client = factory.CreateHttpsClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/pois/prepare-nearby").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/pois/{id}/prepare-content").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/content-preparations/{id}").TryGetProperty("get", out _));
    }

    [Fact]
    public async Task Background_pipeline_persists_foreign_content_and_second_POST_reuses_it_with_no_extra_calls()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true };
        await factory.SeedAdminAsync();
        var id = await AddPoi(factory);
        using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync($"/api/pois/{id}/prepare-content", new { language = "en" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var admitted = (await response.Content.ReadFromJsonAsync<PreparationItemResponse>())!;
        var queue = factory.Services.GetRequiredService<IContentPreparationQueue>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (queue.Find(admitted.JobId!.Value)!.FinishedAt is null)
            await Task.Delay(20, timeout.Token);
        Assert.Equal("Ready", queue.Find(admitted.JobId.Value)!.Status);
        var ready = (await client.GetFromJsonAsync<PreparationItemResponse>(admitted.StatusUrl))!;
        Assert.True(ready.AudioReady);
        Assert.False(ready.Content!.IsFallback);
        Assert.Equal("en", ready.Content.ActualLanguage);
        Assert.NotNull(ready.Content.Audio);
        using var again = await client.PostAsJsonAsync($"/api/pois/{id}/prepare-content", new { language = "en" });
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(1, factory.GenerationHttp.TranslationCalls);
        Assert.Equal(1, factory.GenerationHttp.AudioCalls);
        Assert.Equal(1, factory.GenerationHttp.UploadCalls);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Translations.CountAsync());
        Assert.Equal(1, await db.Audios.CountAsync());
    }

    [Fact]
    public async Task Full_queue_returns_429_without_creating_new_job()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true, StartPreparationWorker = false };
        await factory.SeedAdminAsync();
        var id = await AddPoi(factory);
        var queue = factory.Services.GetRequiredService<IContentPreparationQueue>();
        for (var i = 0; i < 32; i++) queue.Enqueue(1000 + i, "vi", "fixture-revision", true, false);
        using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync($"/api/pois/{id}/prepare-content", new { language = "en" });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var full = (await response.Content.ReadFromJsonAsync<PreparationItemResponse>())!;
        Assert.Equal("QueueFull", full.Status);
        Assert.Null(full.JobId);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Equal(0, factory.GenerationHttp.TranslationCalls + factory.GenerationHttp.AudioCalls);
    }

    [Theory]
    [InlineData("DailyQuotaExceeded", false)]
    [InlineData("QuotaUnavailable", true)]
    public async Task Quota_response_is_explicit_and_zero_limit_does_not_suggest_automatic_retry(string code, bool manual)
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true, StartPreparationWorker = false };
        await factory.SeedAdminAsync();
        var id = await AddPoi(factory);
        factory.Services.GetRequiredService<IContentPreparationQueue>().Block(code,
            manual ? DateTimeOffset.MaxValue : DateTimeOffset.UtcNow.AddMinutes(30));
        using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync($"/api/pois/{id}/prepare-content", new { language = "en" });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<PreparationItemResponse>())!;
        Assert.Equal("QuotaExceeded", result.Status);
        Assert.Equal(code, result.ErrorCode);
        Assert.Equal(manual, result.RequiresOperatorAction);
        if (manual) Assert.Null(response.Headers.RetryAfter);
        else Assert.NotNull(response.Headers.RetryAfter);
        Assert.Equal(0, factory.GenerationHttp.TranslationCalls + factory.GenerationHttp.AudioCalls);
    }
}
