using System.Collections.Concurrent;
using AutoDubbing.Data.Entities;
using AutoDubbing.Data.Repositories;
using AutoDubbing.Data.Storage;
using AutoDubbing.Service.ContentGeneration;
using AutoDubbing.Service.Languages;
using Xunit;

namespace AutoDubbing.Tests;

public class GenerationConcurrencyTests
{
    private sealed class Repository : IPoiReadRepository, IContentGenerationRepository
    {
        public int FailAudioSaves;
        private readonly ConcurrentDictionary<int, PoiAudio> audios = new();
        public Task<PointOfInterest?> FindPublishedAsync(int id, CancellationToken token) =>
            Task.FromResult<PointOfInterest?>(new() { Id = id, Name = "Fixture", OriginalDescription = "Nguồn.", OriginalLanguage = "vi", Status = "Published" });
        public Task<PoiAudio?> FindReadyAudioAsync(int id, string lang, CancellationToken token) => Task.FromResult(audios.GetValueOrDefault(id));
        public Task<PoiTranslation?> FindReadyTranslationAsync(int id, string lang, CancellationToken token) => Task.FromResult<PoiTranslation?>(null);
        public Task<bool> SaveAudioAsync(int id, string lang, string text, string source, string url, int seconds, CancellationToken token, string? expectedVersion = null)
        {
            if (FailAudioSaves-- > 0) throw new IOException("Fixture persistence failure");
            audios[id] = new() { PointOfInterestId = id, Language = lang, FilePath = url, Status = "Ready" };
            return Task.FromResult(true);
        }
        public Task<bool> SaveTranslationAsync(int id, string lang, string text, string source, CancellationToken token, string? expectedVersion = null) => throw new InvalidOperationException("vi must not translate");
        public Task<List<PointOfInterest>> ListPublishedAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<List<PointOfInterest>> FindPublishedByIdsAsync(int[] ids, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Storage : IAudioStorage
    {
        public List<string> Paths { get; } = [];
        public bool IsConfigured => true;
        public Task<string> UploadAsync(string path, byte[] wav, CancellationToken token)
        { Paths.Add(path); return Task.FromResult("https://fixture.test/" + path); }
    }
    private sealed class Provider(int expectedActive) : IContentProvider
    {
        public bool IsConfigured => true;
        public int Calls;
        public int Peak;
        private int active;
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> TranslateAsync(string source, string poiName, SupportedLanguage target, CancellationToken token) =>
            throw new InvalidOperationException("vi must not translate");
        public async Task<GeneratedAudio> SpeakAsync(string text, SupportedLanguage language, CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            var current = Interlocked.Increment(ref active);
            int previous;
            do { previous = Peak; } while (current > previous && Interlocked.CompareExchange(ref Peak, current, previous) != previous);
            if (current >= expectedActive) Reached.TrySetResult();
            try
            {
                await Release.Task.WaitAsync(token);
                return WavAudio.FromProvider(new byte[48000], "audio/l16;rate=24000");
            }
            finally { Interlocked.Decrement(ref active); }
        }
    }
    [Fact]
    public async Task Persistence_failure_retries_same_cached_rendition_without_tts_then_reuses_ready()
    {
        var repo = new Repository { FailAudioSaves = 1 };
        var provider = new Provider(1);
        provider.Release.TrySetResult();
        var storage = new Storage();
        var service = new ContentGenerationService(repo, repo, provider, storage, new MemoryAudioCache(), new GenerationCoordinator());
        var first = await service.GenerateAsync(2, ["vi"], 1, default);
        Assert.Equal("PersistenceUnavailable", Assert.Single(first!.Results).ErrorCode);
        Assert.Equal("persistence", Assert.Single(first.Results).FailureStage);
        Assert.False(Assert.Single(first.Results).AudioReady);
        var second = await service.GenerateAsync(2, ["vi"], 1, default);
        Assert.Equal("Completed", Assert.Single(second!.Results).Status);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(2, storage.Paths.Count);
        Assert.Equal(storage.Paths[0], storage.Paths[1]);
        var third = await service.GenerateAsync(2, ["vi"], 1, default);
        Assert.Equal("SkippedReady", Assert.Single(third!.Results).Status);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(2, storage.Paths.Count);
    }

    [Fact]
    public async Task Concurrent_requests_including_admin_never_run_more_than_one_generation_task()
    {
        var repo = new Repository();
        var provider = new Provider(1);
        var service = new ContentGenerationService(repo, repo, provider, new Storage(), new MemoryAudioCache(), new GenerationCoordinator());
        var calls = Enumerable.Range(1, 3).Select(id => service.GenerateAsync(id, ["vi"], 1, default)).ToArray();
        try
        {
            await provider.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, provider.Calls);
            Assert.Equal(1, provider.Peak);
        }
        finally { provider.Release.TrySetResult(); }
        var results = await Task.WhenAll(calls);
        Assert.All(results, result => Assert.Equal("Completed", Assert.Single(result!.Results).Status));
        Assert.Equal(3, provider.Calls);
        Assert.Equal(1, provider.Peak);
    }
    [Fact]
    public async Task Duplicate_concurrent_requests_for_same_poi_do_not_generate_twice()
    {
        var repo = new Repository();
        var provider = new Provider(1);
        var service = new ContentGenerationService(repo, repo, provider, new Storage(), new MemoryAudioCache(), new GenerationCoordinator());
        var first = service.GenerateAsync(1, ["vi"], 1, default);
        await provider.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = service.GenerateAsync(1, ["vi"], 1, default);
        provider.Release.TrySetResult();
        Assert.Equal("Completed", Assert.Single((await first)!.Results).Status);
        Assert.Equal("SkippedReady", Assert.Single((await second)!.Results).Status);
        Assert.Equal(1, provider.Calls);
    }
}
