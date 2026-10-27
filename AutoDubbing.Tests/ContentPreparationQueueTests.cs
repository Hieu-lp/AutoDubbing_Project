using AutoDubbing.Service.ContentPreparation;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoDubbing.Tests;

public sealed class ContentPreparationQueueTests
{
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Snapshot_prunes_expired_failure_details_and_counters_without_new_admission()
    {
        var clock = new Clock();
        var queue = new ContentPreparationQueue(Options.Create(new ContentPreparationOptions()), clock);
        var job = queue.Enqueue(1, "en", "a", false, true).Job!;
        await queue.TakeAsync(default);
        queue.Complete(job.Id, "Failed", false, "GeminiTimeout", failureStage: "translation");
        Assert.Equal(1, queue.Snapshot().Failed);
        Assert.Equal("translation", Assert.Single(queue.Snapshot().Jobs).FailureStage);
        clock.Now = clock.Now.AddMinutes(31);
        var expired = queue.Snapshot();
        Assert.Equal(0, expired.Failed);
        Assert.Empty(expired.Jobs);
        Assert.Equal(30, expired.RetentionMinutes);
    }

    [Fact]
    public async Task Concurrent_admissions_deduplicate_by_poi_language_and_source_revision()
    {
        var queue = new ContentPreparationQueue(Options.Create(new ContentPreparationOptions()), new Clock());
        var results = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
            queue.Enqueue(1, "vi", "revision-a", true, false))));
        Assert.Single(results.Select(result => result.Job!.Id).Distinct());
        Assert.NotEqual(results[0].Job!.Id, queue.Enqueue(1, "en", "revision-a", false, true).Job!.Id);
        Assert.NotEqual(results[0].Job!.Id, queue.Enqueue(1, "vi", "revision-b", true, false).Job!.Id);
        Assert.NotEqual(results[0].Job!.Id, queue.Enqueue(2, "vi", "revision-a", true, false).Job!.Id);
    }

    [Fact]
    public async Task Nearby_is_fifo_and_interactive_promotes_existing_job_without_duplication()
    {
        var queue = new ContentPreparationQueue(Options.Create(new ContentPreparationOptions()), new Clock());
        var nearest = queue.Enqueue(1, "vi", "a", true, false).Job!;
        var farther = queue.Enqueue(2, "vi", "a", true, false).Job!;
        var promoted = queue.Enqueue(3, "vi", "a", true, false).Job!;
        Assert.Equal(promoted.Id, queue.Enqueue(3, "vi", "a", true, true).Job!.Id);
        var chosen = queue.Enqueue(4, "en", "a", false, true).Job!;
        Assert.Equal(promoted.Id, (await queue.TakeAsync(default)).Id);
        Assert.Equal(chosen.Id, (await queue.TakeAsync(default)).Id);
        Assert.Equal(nearest.Id, (await queue.TakeAsync(default)).Id);
        Assert.Equal(farther.Id, (await queue.TakeAsync(default)).Id);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.TakeAsync(timeout.Token));
    }

    [Fact]
    public async Task Capacity_includes_running_work_and_duplicate_can_still_join_when_full()
    {
        var queue = new ContentPreparationQueue(Options.Create(new ContentPreparationOptions { QueueCapacity = 1 }), new Clock());
        var first = queue.Enqueue(1, "vi", "a", true, false).Job!;
        await queue.TakeAsync(default);
        Assert.Equal(first.Id, queue.Enqueue(1, "vi", "a", true, true).Job!.Id);
        Assert.Equal("QueueFull", queue.Enqueue(2, "vi", "a", true, false).ErrorCode);
        queue.Complete(first.Id, "Ready", true);
        Assert.NotNull(queue.Enqueue(2, "vi", "a", true, false).Job);
    }

    [Fact]
    public async Task Failure_cooldown_and_system_quota_block_stop_new_jobs_until_due()
    {
        var clock = new Clock();
        var queue = new ContentPreparationQueue(Options.Create(new ContentPreparationOptions()), clock);
        var first = queue.Enqueue(1, "en", "a", false, true).Job!;
        await queue.TakeAsync(default);
        queue.Complete(first.Id, "QuotaExceeded", true, "DailyQuotaExceeded", clock.Now.AddMinutes(30));
        queue.Block("DailyQuotaExceeded", clock.Now.AddMinutes(30));
        Assert.Equal(first.Id, queue.Enqueue(1, "en", "a", true, true).Job!.Id);
        Assert.Equal("DailyQuotaExceeded", queue.Enqueue(2, "vi", "a", true, false).ErrorCode);
        clock.Now = clock.Now.AddMinutes(31);
        Assert.NotEqual(first.Id, queue.Enqueue(1, "en", "a", true, true).Job!.Id);
    }

    [Fact]
    public async Task Completed_job_history_is_bounded_and_expires()
    {
        var clock = new Clock();
        var queue = new ContentPreparationQueue(Options.Create(new ContentPreparationOptions { RetainedJobs = 1 }), clock);
        var first = queue.Enqueue(1, "vi", "a", true, false).Job!;
        await queue.TakeAsync(default);
        queue.Complete(first.Id, "Ready", true);
        clock.Now = clock.Now.AddSeconds(1);
        var second = queue.Enqueue(2, "vi", "a", true, false).Job!;
        await queue.TakeAsync(default);
        queue.Complete(second.Id, "Ready", true);
        Assert.Null(queue.Find(first.Id));
        Assert.NotNull(queue.Find(second.Id));
        clock.Now = clock.Now.AddMinutes(31);
        Assert.Null(queue.Find(second.Id));
    }

    [Fact]
    public void Per_client_and_global_limits_share_a_bounded_window_and_reset()
    {
        var clock = new Clock();
        var gate = new PreparationRateGate(Options.Create(new ContentPreparationOptions
            { ClientRequestsPerMinute = 2, GlobalRequestsPerMinute = 3 }), clock);
        Assert.True(gate.TryAcquire("a", out _));
        Assert.True(gate.TryAcquire("a", out _));
        Assert.False(gate.TryAcquire("a", out var wait));
        Assert.Equal(60, wait);
        Assert.True(gate.TryAcquire("b", out _));
        Assert.False(gate.TryAcquire("c", out _));
        clock.Now = clock.Now.AddMinutes(1);
        Assert.True(gate.TryAcquire("a", out _));
    }
}
