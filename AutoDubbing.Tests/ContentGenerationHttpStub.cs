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
    public ConcurrentQueue<string> SpeechInputs { get; } = new();
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var url = request.RequestUri!;
        if (url.Host == "fixture.supabase.co")
        {
            if (request.Method == HttpMethod.Get) return Json(new { @public = true });
            Interlocked.Increment(ref UploadCalls);
            if (NextUploadError is { } error) { NextUploadError = null; return new(error); }
            Assert.Equal("audio/wav", request.Content!.Headers.ContentType!.MediaType);
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
