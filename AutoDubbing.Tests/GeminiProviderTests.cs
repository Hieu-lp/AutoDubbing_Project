using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AutoDubbing.Service.ContentGeneration;
using AutoDubbing.Service.Languages;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoDubbing.Tests;

public class GeminiProviderTests
{
    private sealed class HttpStub(Func<int, HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(respond(++Calls, request));
    }
    private static HttpResponseMessage Success() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"Translated."}]}}]}""",
            Encoding.UTF8, "application/json")
    };
    private static GeminiContentProvider Provider(HttpStub http, RecordingGenerationDelay wait, int retries = 2) =>
        new(new HttpClient(http), Options.Create(new GeminiOptions { ApiKey = "secret-test-value", MaxRetries = retries }),
            wait, new GenerationCoordinator());

    [Fact]
    public async Task Rate_limit_waits_retry_after_then_succeeds()
    {
        var http = new HttpStub((call, request) =>
        {
            Assert.Empty(request.RequestUri!.Query);
            Assert.Equal("secret-test-value", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            if (call > 1) return Success();
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));
            return response;
        });
        var wait = new RecordingGenerationDelay();
        var text = await Provider(http, wait).TranslateAsync("Source.", "POI", LanguageCatalog.Find("en")!, default);
        Assert.Equal("Translated.", text);
        Assert.Equal(2, http.Calls);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(wait.Delays));
    }
    [Fact]
    public async Task Repeated_429_stops_after_finite_retries_and_cools_later_requests()
    {
        var http = new HttpStub((_, _) => new(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}") });
        var wait = new RecordingGenerationDelay();
        var provider = Provider(http, wait);
        for (var call = 0; call < 2; call++)
        {
            var error = await Assert.ThrowsAsync<GenerationException>(() =>
                provider.TranslateAsync("Source.", "POI", LanguageCatalog.Find("en")!, default));
            Assert.Equal("QuotaExceeded", error.Code);
        }
        Assert.Equal(3, http.Calls);
        Assert.Equal(2, wait.Delays.Count);
        Assert.All(wait.Delays, delay => Assert.True(delay > TimeSpan.Zero));
    }
    [Fact]
    public async Task Daily_or_zero_quota_does_not_retry_or_spin()
    {
        var http = new HttpStub((_, _) => new(HttpStatusCode.TooManyRequests) { Content = new StringContent(
            """{"error":{"message":"secret-test-value","details":[{"violations":[{"quotaId":"RequestsPerDay","quotaValue":"0"}]}]}}""") });
        var wait = new RecordingGenerationDelay();
        var error = await Assert.ThrowsAsync<GenerationException>(() => Provider(http, wait)
            .TranslateAsync("Source.", "POI", LanguageCatalog.Find("en")!, default));
        Assert.Equal("QuotaExceeded", error.Code);
        Assert.DoesNotContain("secret-test-value", error.ToString());
        Assert.Equal(1, http.Calls);
        Assert.Empty(wait.Delays);
    }
    [Theory]
    [InlineData(401, "GeminiUnauthorized")]
    [InlineData(403, "GeminiForbidden")]
    [InlineData(404, "GeminiModelUnavailable")]
    public async Task Permanent_errors_are_sanitized_and_not_retried(int status, string code)
    {
        var http = new HttpStub((_, _) => new((HttpStatusCode)status)
            { Content = new StringContent("secret-test-value must not be exposed") });
        var wait = new RecordingGenerationDelay();
        var error = await Assert.ThrowsAsync<GenerationException>(() => Provider(http, wait)
            .TranslateAsync("Source.", "POI", LanguageCatalog.Find("en")!, default));
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain("secret-test-value", error.ToString());
        Assert.Equal(1, http.Calls);
        Assert.Empty(wait.Delays);
    }
    [Fact]
    public void Pcm_is_wrapped_once_and_duration_comes_from_frames()
    {
        var pcm = WavAudio.FromProvider(new byte[48002], "audio/l16;rate=24000;channels=1");
        Assert.Equal(1 + 1 / 24000d, pcm.DurationSeconds, 10);
        var wav = WavAudio.FromProvider(pcm.Wav, "audio/wav");
        Assert.Equal(pcm.Wav, wav.Wav);
        Assert.Equal(pcm.DurationSeconds, wav.DurationSeconds);
        Assert.Throws<GenerationException>(() => WavAudio.FromProvider([1], "audio/l16;rate=24000"));
        Assert.Throws<GenerationException>(() => WavAudio.FromProvider([1, 2], "audio/l16"));
        Assert.Throws<GenerationException>(() => WavAudio.FromProvider(pcm.Wav[..^2], "audio/wav"));
    }
    [Theory]
    [InlineData("zh-hans", "Nhà thờ Đức Bà于1880年落成。")]
    [InlineData("ja", "Nhà thờ Đức Bàは1880年に完成しました。")]
    [InlineData("ko", "Nhà thờ Đức Bà는1880년에완공되었습니다.")]
    public async Task Year_adjacent_to_non_latin_letters_is_preserved(string language, string translated)
    {
        using var http = new HttpStub((_, _) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new {
                finishReason = "STOP", content = new { parts = new[] { new { text = translated } } }
            } } }), Encoding.UTF8, "application/json")
        });
        var result = await Provider(http, new RecordingGenerationDelay()).TranslateAsync(
            "Nhà thờ Đức Bà khánh thành năm 1880.", "Nhà thờ Đức Bà", LanguageCatalog.Find(language)!, default);
        Assert.Equal(translated, result);
    }
    [Theory]
    [InlineData("Nhà thờ Đức Bà于18800年落成。")]
    [InlineData("Nhà thờ Đức Bà于1881年落成。")]
    [InlineData("Nhà thờ Đức Bà已落成。")]
    public async Task Incorrect_or_missing_year_still_requires_review(string translated)
    {
        using var http = new HttpStub((_, _) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new {
                finishReason = "STOP", content = new { parts = new[] { new { text = translated } } }
            } } }), Encoding.UTF8, "application/json")
        });
        var error = await Assert.ThrowsAsync<GenerationException>(() =>
            Provider(http, new RecordingGenerationDelay()).TranslateAsync("Nhà thờ Đức Bà khánh thành năm 1880.",
                "Nhà thờ Đức Bà", LanguageCatalog.Find("zh-hans")!, default));
        Assert.Equal("TranslationRequiresReview", error.Code);
    }

    [Fact]
    public async Task Every_catalog_language_uses_its_own_tts_configuration_and_exact_transcript()
    {
        var calls = 0;
        using var http = new HttpStub((call, request) =>
        {
            using var json = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            var expected = LanguageCatalog.All[calls++];
            var generation = json.RootElement.GetProperty("generationConfig");
            var speech = generation.GetProperty("speechConfig");
            Assert.Equal(expected.Voice, speech.GetProperty("voiceConfig").GetProperty("prebuiltVoiceConfig").GetProperty("voiceName").GetString());
            if (expected.TtsLanguageCode is null) Assert.False(speech.TryGetProperty("languageCode", out _));
            else Assert.Equal(expected.TtsLanguageCode, speech.GetProperty("languageCode").GetString());
            Assert.Contains("<transcript>\nExact input.\n</transcript>",
                json.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
            return new(HttpStatusCode.OK) { Content = new StringContent(
                """{"candidates":[{"finishReason":"STOP","content":{"parts":[{"inlineData":{"mimeType":"audio/l16;rate=24000","data":"AAA="}}]}}]}""") };
        });
        var provider = Provider(http, new RecordingGenerationDelay());
        foreach (var language in LanguageCatalog.All) await provider.SpeakAsync("Exact input.", language, default);
        Assert.Equal(15, http.Calls);
    }
}
