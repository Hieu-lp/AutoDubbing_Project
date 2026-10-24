using System.Collections.Concurrent;
using AutoDubbing.API.Background;
using AutoDubbing.Service.ContentGeneration;
using AutoDubbing.Service.ContentPreparation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoDubbing.Tests;

public sealed class ContentPreparationWorkerTests
{
    private sealed class ObservedQueue(IContentPreparationQueue inner) : IContentPreparationQueue
    {
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<PreparationJob>> finished = new();
        public (string Code, DateTimeOffset Until)? CurrentBlock => inner.CurrentBlock;
        public QueueAdmission Enqueue(int poiId, string language, string sourceVersion, bool textReady, bool interactive) =>
            inner.Enqueue(poiId, language, sourceVersion, textReady, interactive);
        public PreparationJob? Find(Guid id) => inner.Find(id);
        public Task<PreparationJob> TakeAsync(CancellationToken token) => inner.TakeAsync(token);
        public void Block(string code, DateTimeOffset until) => inner.Block(code, until);
        public void Complete(Guid id, string status, bool textReady, string? errorCode = null, DateTimeOffset? retryAt = null)
        {
            inner.Complete(id, status, textReady, errorCode, retryAt);
            Completion(id).TrySetResult(inner.Find(id)!);
        }
        private TaskCompletionSource<PreparationJob> Completion(Guid id) => finished.GetOrAdd(id,
            _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        public Task<PreparationJob> Wait(Guid id) => Completion(id).Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private sealed class Generator(Func<int, string, CancellationToken, Task<LanguageGenerationResult>> generate)
        : IContentGenerationService
    {
        public bool IsConfigured => true;
        public int Calls;
        public async Task<GenerationBatchResult?> GenerateAsync(int poiId, IReadOnlyList<string> languages,
            int batchSize, CancellationToken token, string? expectedSourceVersion = null)
        {
            Assert.Equal(1, batchSize);
            Assert.Single(languages);
            Assert.NotNull(expectedSourceVersion);
            Interlocked.Increment(ref Calls);
            var result = await generate(poiId, languages[0], token);
            return new(poiId, [result], result.AudioReady ? [] : [result.Language]);
        }
    }

    [Fact]
    public async Task One_worker_runs_one_job_at_a_time_with_selected_language_first_then_nearest_fifo()
    {
        var options = Options.Create(new ContentPreparationOptions());
        var queue = new ObservedQueue(new ContentPreparationQueue(options, TimeProvider.System));
        var order = new ConcurrentQueue<int>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generator = new Generator(async (id, language, token) =>
        {
            order.Enqueue(id);
            if (id == 3) { entered.TrySetResult(); await release.Task.WaitAsync(token); }
            return new(language, "Completed", true, true);
        });
        using var services = new ServiceCollection().AddSingleton<IContentGenerationService>(generator).BuildServiceProvider();
        using var worker = new ContentPreparationWorker(queue, services.GetRequiredService<IServiceScopeFactory>(),
            options, TimeProvider.System, NullLogger<ContentPreparationWorker>.Instance);
        var nearest = queue.Enqueue(1, "vi", "a", true, false).Job!;
        var farther = queue.Enqueue(2, "vi", "a", true, false).Job!;
        var chosen = queue.Enqueue(3, "en", "a", false, true).Job!;
        await worker.StartAsync(default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, generator.Calls);
            Assert.Equal(chosen.Id, queue.Enqueue(3, "en", "a", false, true).Job!.Id);
            release.TrySetResult();
            await Task.WhenAll(queue.Wait(chosen.Id), queue.Wait(nearest.Id), queue.Wait(farther.Id));
            Assert.Equal(new[] { 3, 1, 2 }, order);
            Assert.Equal(3, generator.Calls);
        }
        finally { release.TrySetResult(); await worker.StopAsync(default); }
    }

    [Theory]
    [InlineData("DailyLimit", "DailyQuotaExceeded")]
    [InlineData("ZeroLimit", "QuotaUnavailable")]
    [InlineData("RateLimit", "QuotaExceeded")]
    public async Task Quota_stops_other_queued_jobs_without_more_provider_calls(string kind, string code)
    {
        var clock = new ContentPreparationQueueTests.Clock();
        var options = Options.Create(new ContentPreparationOptions());
        var queue = new ObservedQueue(new ContentPreparationQueue(options, clock));
        var generator = new Generator((_, language, _) => Task.FromResult(new LanguageGenerationResult(language,
            "PausedQuota", true, false, "QuotaExceeded", FailureDetails: new(GeminiOptions.AudioModel, 429,
                QuotaKind: kind, RetryAfterSeconds: 90))));
        using var services = new ServiceCollection().AddSingleton<IContentGenerationService>(generator).BuildServiceProvider();
        using var worker = new ContentPreparationWorker(queue, services.GetRequiredService<IServiceScopeFactory>(),
            options, clock, NullLogger<ContentPreparationWorker>.Instance);
        var first = queue.Enqueue(1, "vi", "a", true, false).Job!;
        var second = queue.Enqueue(2, "vi", "a", true, false).Job!;
        await worker.StartAsync(default);
        try
        {
            var result = await queue.Wait(first.Id);
            var deferred = await queue.Wait(second.Id);
            Assert.Equal("QuotaExceeded", result.Status);
            Assert.Equal(code, result.ErrorCode);
            Assert.Equal(code, deferred.ErrorCode);
            Assert.False(result.Status == "Ready");
            Assert.Equal(1, generator.Calls);
            Assert.Equal(code, queue.Enqueue(3, "vi", "a", true, false).ErrorCode);
            Assert.Equal(kind == "ZeroLimit" ? DateTimeOffset.MaxValue
                : kind == "DailyLimit" ? clock.Now.AddMinutes(30) : clock.Now.AddSeconds(90), result.RetryAt);
        }
        finally { await worker.StopAsync(default); }
    }

    [Theory]
    [InlineData("GeminiForbidden")]
    [InlineData("GeminiUnauthorized")]
    [InlineData("GeminiModelUnavailable")]
    public async Task Access_failure_requires_operator_action_and_is_not_reported_as_daily_quota(string code)
    {
        var options = Options.Create(new ContentPreparationOptions());
        var queue = new ObservedQueue(new ContentPreparationQueue(options, TimeProvider.System));
        var generator = new Generator((_, language, _) => Task.FromResult(new LanguageGenerationResult(language,
            "Failed", false, false, code)));
        using var services = new ServiceCollection().AddSingleton<IContentGenerationService>(generator).BuildServiceProvider();
        using var worker = new ContentPreparationWorker(queue, services.GetRequiredService<IServiceScopeFactory>(),
            options, TimeProvider.System, NullLogger<ContentPreparationWorker>.Instance);
        var first = queue.Enqueue(1, "en", "a", false, true).Job!;
        await worker.StartAsync(default);
        try
        {
            var result = await queue.Wait(first.Id);
            Assert.Equal("Unavailable", result.Status);
            Assert.Equal(code, result.ErrorCode);
            Assert.Equal(code, queue.Enqueue(2, "vi", "a", true, false).ErrorCode);
            Assert.Equal(DateTimeOffset.MaxValue, result.RetryAt);
        }
        finally { await worker.StopAsync(default); }
    }
}
