using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AutoDubbing.Data.Contexts;
using AutoDubbing.Service.Authentication;
using AutoDubbing.Service.ContentGeneration;
using AutoDubbing.Service.Languages;
using AutoDubbing.Service.Pois;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace AutoDubbing.Tests;

public class ContentGenerationTests
{
    private static async Task<HttpClient> Authenticated(AdminApiFactory factory)
    {
        await factory.SeedAdminAsync();
        var client = factory.CreateHttpsClient();
        using var login = await client.PostAsJsonAsync("/api/admin/auth/login", new { username = "Admin", password = factory.Password });
        using var json = System.Text.Json.JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        return client;
    }
    private static async Task<int> Poi(AdminApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
        var admin = await db.Users.FirstAsync();
        var poi = new AutoDubbing.Data.Entities.PointOfInterest { UserId = admin.Id, Name = "Fixture POI",
            OriginalDescription = "Nội dung gốc.", OriginalLanguage = "vi", Status = "Published" };
        db.PointsOfInterest.Add(poi);
        await db.SaveChangesAsync();
        return poi.Id;
    }
    [Fact]
    public async Task Catalog_has_15_and_public_endpoint_exposes_no_provider_credentials()
    {
        await using var factory = new AdminApiFactory();
        using var client = factory.CreateHttpsClient();
        var response = await client.GetStringAsync("/api/languages");
        Assert.Equal(15, LanguageCatalog.All.Count);
        using var json = System.Text.Json.JsonDocument.Parse(response);
        Assert.Equal(15, json.RootElement.GetArrayLength());
        Assert.DoesNotContain("ApiKey", response);
        Assert.DoesNotContain("ttsLanguageCode", response);
        Assert.Equal("cmn-CN", LanguageCatalog.Find("ZH-HANS")!.TtsLanguageCode);
        Assert.Null(LanguageCatalog.Find("ms")!.TtsLanguageCode);
    }
    [Fact]
    public async Task Entire_catalog_runs_in_small_batches_reuses_ready_results_and_keeps_original_vi()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true };
        using var client = await Authenticated(factory);
        var id = await Poi(factory);
        var languages = LanguageCatalog.All.Select(l => l.Code).ToArray();
        GenerationBatchResult? result = null;
        for (var i = 0; i < 5; i++)
        {
            using var response = await client.PostAsJsonAsync($"/api/admin/pois/{id}/generate-content", new { languages, batchSize = 3 });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            result = await response.Content.ReadFromJsonAsync<GenerationBatchResult>();
            Assert.Equal(3, result!.Results.Count(r => r.Status == "Completed"));
        }
        Assert.Empty(result!.RemainingLanguages);
        Assert.Equal(14, factory.GenerationHttp.TranslationCalls);
        Assert.Equal(15, factory.GenerationHttp.AudioCalls);
        Assert.Equal(15, factory.GenerationHttp.UploadCalls);
        foreach (var language in languages)
        {
            var content = await client.GetFromJsonAsync<PoiContentDto>($"/api/pois/{id}/content?language={language}");
            Assert.Equal(language, content!.RequestedLanguage);
            Assert.Equal(language, content.ActualLanguage);
            Assert.False(content.IsFallback);
            Assert.Equal(language, content.Audio!.Language);
        }
        Assert.Contains(factory.GenerationHttp.SpeechInputs, input => input.Contains("<transcript>\nNội dung gốc.\n</transcript>"));
        using var again = await client.PostAsJsonAsync($"/api/admin/pois/{id}/generate-content", new { languages });
        var skipped = await again.Content.ReadFromJsonAsync<GenerationBatchResult>();
        Assert.All(skipped!.Results, r => Assert.Equal("SkippedReady", r.Status));
        Assert.Equal(15, factory.GenerationHttp.AudioCalls);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(14, await db.Translations.CountAsync());
        Assert.Equal(15, await db.Audios.CountAsync());
        Assert.All(await db.Audios.ToArrayAsync(), a => Assert.Equal(2, a.Duration)); // PCM duration 1.000041... rounds up
    }
    [Fact]
    public async Task Translation_survives_audio_failure_and_retry_does_not_translate_again()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true };
        using var client = await Authenticated(factory);
        var id = await Poi(factory);
        factory.GenerationHttp.NextAudioError = HttpStatusCode.Forbidden;
        using var first = await client.PostAsJsonAsync($"/api/admin/pois/{id}/generate-content", new { languages = new[] { "en", "ja" } });
        var result = await first.Content.ReadFromJsonAsync<GenerationBatchResult>();
        Assert.True(result!.Results[0].TextReady);
        Assert.False(result.Results[0].AudioReady);
        Assert.Equal("Deferred", result.Results[1].Status);
        var fallback = await client.GetFromJsonAsync<PoiContentDto>($"/api/pois/{id}/content?language=ja");
        Assert.True(fallback!.IsFallback);
        Assert.Equal("vi", fallback.ActualLanguage);
        using var second = await client.PostAsJsonAsync($"/api/admin/pois/{id}/generate-content", new { languages = new[] { "en" } });
        var completed = await second.Content.ReadFromJsonAsync<GenerationBatchResult>();
        Assert.Equal("Completed", completed!.Results[0].Status);
        Assert.Equal(1, factory.GenerationHttp.TranslationCalls);
        Assert.Equal(2, factory.GenerationHttp.AudioCalls);
    }
    [Fact]
    public async Task Upload_failure_keeps_wav_cache_and_retry_only_uploads()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true };
        using var client = await Authenticated(factory);
        var id = await Poi(factory);
        factory.GenerationHttp.NextUploadError = HttpStatusCode.ServiceUnavailable;
        using var first = await client.PostAsJsonAsync($"/api/admin/pois/{id}/generate-content", new { languages = new[] { "en" } });
        var failed = await first.Content.ReadFromJsonAsync<GenerationBatchResult>();
        Assert.Equal("Failed", failed!.Results[0].Status);
        using var second = await client.PostAsJsonAsync($"/api/admin/pois/{id}/generate-content", new { languages = new[] { "en" } });
        var completed = await second.Content.ReadFromJsonAsync<GenerationBatchResult>();
        Assert.Equal("Completed", completed!.Results[0].Status);
        Assert.Equal(1, factory.GenerationHttp.TranslationCalls);
        Assert.Equal(1, factory.GenerationHttp.AudioCalls);
        Assert.Equal(2, factory.GenerationHttp.UploadCalls);
    }

    [Fact]
    public async Task Unreadable_uploaded_file_is_not_ready_and_retry_reuses_object_and_cache()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true };
        using var client = await Authenticated(factory);
        var id = await Poi(factory);
        factory.GenerationHttp.FailNextPublicReadAfterUpload = true;
        using var first = await client.PostAsJsonAsync($"/api/admin/pois/{id}/generate-content", new { languages = new[] { "en" } });
        var failed = await first.Content.ReadFromJsonAsync<GenerationBatchResult>();
        Assert.False(failed!.Results[0].AudioReady);
        Assert.Equal("VerifyingPublicObject", failed.Results[0].StorageFailureDetails?.Stage);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
        Assert.Equal(0, await db.Audios.CountAsync());
        using var second = await client.PostAsJsonAsync($"/api/admin/pois/{id}/generate-content", new { languages = new[] { "en" } });
        var completed = await second.Content.ReadFromJsonAsync<GenerationBatchResult>();
        Assert.True(completed!.Results[0].AudioReady);
        Assert.Equal(1, factory.GenerationHttp.TranslationCalls);
        Assert.Equal(1, factory.GenerationHttp.AudioCalls);
        Assert.Equal(1, factory.GenerationHttp.UploadCalls);
        Assert.Equal(1, await db.Audios.CountAsync());
        using var again = await client.PostAsJsonAsync($"/api/admin/pois/{id}/generate-content", new { languages = new[] { "en" } });
        var skipped = await again.Content.ReadFromJsonAsync<GenerationBatchResult>();
        Assert.Equal("SkippedReady", skipped!.Results[0].Status);
        Assert.Equal(1, await db.Audios.CountAsync());
        Assert.Equal(1, factory.GenerationHttp.UploadCalls);
    }
    [Fact]
    public async Task Endpoint_requires_token_and_rejects_unknown_duplicate_or_oversized_batch()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true };
        using (var anonymous = factory.CreateHttpsClient())
        using (var denied = await anonymous.PostAsJsonAsync("/api/admin/pois/1/generate-content", new { languages = new[] { "vi" } }))
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var client = await Authenticated(factory);
        foreach (var languages in new[] { new[] { "xx" }, new[] { "en", "EN" }, Array.Empty<string>() })
        {
            using var invalid = await client.PostAsJsonAsync("/api/admin/pois/1/generate-content", new { languages });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var large = await client.PostAsJsonAsync("/api/admin/pois/1/generate-content", new { languages = new[] { "vi" }, batchSize = 15 });
        Assert.Equal(HttpStatusCode.BadRequest, large.StatusCode);
        Assert.Equal(0, factory.GenerationHttp.AudioCalls);
    }
    [Fact]
    public async Task Quota_keeps_prior_languages_and_translation_then_resumes_only_missing_audio()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true };
        using var client = await Authenticated(factory);
        var id = await Poi(factory);
        using var vi = await client.PostAsJsonAsync($"/api/admin/pois/{id}/generate-content", new { languages = new[] { "vi" } });
        factory.GenerationHttp.NextAudioError = HttpStatusCode.TooManyRequests;
        using var limited = await client.PostAsJsonAsync($"/api/admin/pois/{id}/generate-content", new { languages = new[] { "vi", "en", "ja" } });
        var result = await limited.Content.ReadFromJsonAsync<GenerationBatchResult>();
        Assert.Equal("SkippedReady", result!.Results[0].Status);
        Assert.Equal("PausedQuota", result.Results[1].Status);
        Assert.True(result.Results[1].TextReady);
        Assert.Equal("Deferred", result.Results[2].Status);
        var calls = factory.GenerationHttp.AudioCalls;
        using var cooling = await client.PostAsJsonAsync($"/api/admin/pois/{id}/generate-content", new { languages = new[] { "en" } });
        Assert.Equal(calls, factory.GenerationHttp.AudioCalls);
        factory.Services.GetRequiredService<GenerationCoordinator>().Pause(GeminiOptions.AudioModel, TimeSpan.Zero);
        using var resumed = await client.PostAsJsonAsync($"/api/admin/pois/{id}/generate-content", new { languages = new[] { "vi", "en", "ja" } });
        var completed = await resumed.Content.ReadFromJsonAsync<GenerationBatchResult>();
        Assert.Empty(completed!.RemainingLanguages);
        Assert.Equal(2, factory.GenerationHttp.TranslationCalls);
        Assert.Equal(4, factory.GenerationHttp.AudioCalls);
        Assert.Equal(3, factory.GenerationHttp.UploadCalls);
    }
    [Fact]
    public async Task Locked_admin_cannot_generate_using_existing_token()
    {
        await using var factory = new AdminApiFactory { GenerationConfigured = true };
        using var client = await Authenticated(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var id = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Select(user => user.Id).SingleAsync();
        await factory.ChangeStatusAsync(id, "Locked");
        using var response = await client.PostAsJsonAsync("/api/admin/pois/1/generate-content", new { languages = new[] { "en" } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.GenerationHttp.TranslationCalls);
    }

    [Fact]
    public async Task Missing_storage_configuration_is_503_without_provider_requests()
    {
        await using var factory = new AdminApiFactory();
        using var client = await Authenticated(factory);
        using var response = await client.PostAsJsonAsync("/api/admin/pois/1/generate-content", new { languages = new[] { "en" } });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, factory.GenerationHttp.TranslationCalls);
    }
}
