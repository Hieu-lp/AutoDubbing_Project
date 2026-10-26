using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using AutoDubbing.Data.Storage;
using AutoDubbing.Service.ContentGeneration;

using Xunit;

namespace AutoDubbing.Tests;

internal sealed class ContentGenerationHttpStub : HttpMessageHandler
{
    public int TranslationCalls;
    public int AudioCalls;
    public int UploadCalls;
    public HttpStatusCode? NextAudioError { get; set; }
    public HttpStatusCode? NextUploadError { get; set; }
    public bool FailNextPublicReadAfterUpload { get; set; }
    private readonly ConcurrentDictionary<string, byte[]> objects = new();
    public ConcurrentQueue<string> SpeechInputs { get; } = new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var url = request.RequestUri!;
        if (url.Host is "fixture.supabase.co" or "fixture.storage.supabase.co")
        {
            Assert.Equal(request.Method == HttpMethod.Post ? "fixture.storage.supabase.co" : "fixture.supabase.co", url.Host);
            if (request.Method == HttpMethod.Get)
            {
                if (url.AbsolutePath.Contains("/bucket/", StringComparison.Ordinal)) return Json(new { @public = true });
                Assert.False(request.Headers.Contains("apikey"));
                Assert.Null(request.Headers.Authorization);
                var path = url.AbsolutePath["/storage/v1/object/public/fixture-audio/".Length..];
                if (objects.TryGetValue(path, out var data))
                {
                    if (FailNextPublicReadAfterUpload)
                    {
                        FailNextPublicReadAfterUpload = false;
                        return new(HttpStatusCode.BadRequest) { Content = new StringContent("""{"code":"NoSuchKey"}""") };
                    }
                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
                    response.Content.Headers.ContentType = new("audio/wav");
                    return response;
                }
                return new(HttpStatusCode.BadRequest) { Content = new StringContent("""{"code":"NoSuchKey"}""") };
            }
            Interlocked.Increment(ref UploadCalls);
            if (NextUploadError is { } error) { NextUploadError = null; return new(error); }
            Assert.Equal("audio/wav", request.Content!.Headers.ContentType!.MediaType);
            objects[url.AbsolutePath["/storage/v1/object/fixture-audio/".Length..]] = await request.Content.ReadAsByteArrayAsync(token);
            return Json(new { Key = "fixture" });
        }
        Assert.Equal("generativelanguage.googleapis.com", url.Host);
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        if (url.AbsolutePath.Contains(GeminiOptions.TranslationModel, StringComparison.Ordinal))
        {
            Interlocked.Increment(ref TranslationCalls);
            return Json(new { candidates = new[] { new { finishReason = "STOP", content = new {
                parts = new[] { new { text = "Fixture translation (not real model output)." } } } } } });
        }
        Assert.Contains(GeminiOptions.AudioModel, url.AbsolutePath);
        Interlocked.Increment(ref AudioCalls);
        SpeechInputs.Enqueue(json.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!);
        if (NextAudioError is { } audioError) { NextAudioError = null; return new(audioError); }
        return Json(new { candidates = new[] { new { finishReason = "STOP", content = new {
            parts = new[] { new { inlineData = new { mimeType = "audio/l16;rate=24000;channels=1",
                data = Convert.ToBase64String(new byte[48002]) } } } } } } });
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}

internal sealed class MemoryAudioCache : IAudioCache
{
    private readonly ConcurrentDictionary<string, byte[]> files = new();
    public Task<byte[]?> ReadAsync(string path, CancellationToken token) => Task.FromResult(files.GetValueOrDefault(path));
    public Task WriteAsync(string path, byte[] wav, CancellationToken token) { files[path] = wav; return Task.CompletedTask; }
    public void Remove(string path) => files.TryRemove(path, out _);
}

internal sealed class RecordingGenerationDelay : IGenerationDelay
{
    public List<TimeSpan> Delays { get; } = [];
    public Task WaitAsync(TimeSpan delay, CancellationToken token) { token.ThrowIfCancellationRequested(); Delays.Add(delay); return Task.CompletedTask; }
}
