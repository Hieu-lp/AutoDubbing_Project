using Microsoft.Extensions.Options;

namespace AutoDubbing.Service.ContentPreparation;

// Single-process bounded queue. Only metadata is kept here; SQL/cache/storage hold successful work.
public sealed class ContentPreparationQueue(IOptions<ContentPreparationOptions> options, TimeProvider clock)
    : IContentPreparationQueue
{
    private readonly object gate = new();
    private readonly SemaphoreSlim available = new(0);
    private readonly LinkedList<Guid> interactive = new();
    private readonly LinkedList<Guid> nearby = new();
    private readonly Dictionary<Guid, PreparationJob> jobs = new();
    private readonly Dictionary<(int, string, string), Guid> keys = new();
    private (string Code, DateTimeOffset Until)? block;
    private readonly DateTimeOffset startedAt = clock.GetUtcNow();

    public (string Code, DateTimeOffset Until)? CurrentBlock
    {
        get { lock (gate) return block is { } value && value.Until > clock.GetUtcNow() ? value : null; }
    }

    public QueueAdmission Enqueue(int poiId, string language, string sourceVersion, bool textReady, bool isInteractive)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            Prune(now);
            var key = (poiId, language, sourceVersion);
            if (keys.TryGetValue(key, out var existingId))
            {
                var existing = jobs[existingId];
                if (existing.FinishedAt is null || existing.RetryAt > now)
                {
                    if (isInteractive && !existing.Interactive && existing.Status == "Queued")
                    {
                        nearby.Remove(existingId);
                        interactive.AddLast(existingId);
                        jobs[existingId] = existing = existing with { Interactive = true };
                    }
                    return new(existing);
                }
                // A terminal job does not prove that content is still current. The service rechecks SQL.
                keys.Remove(key);
            }
            if (CurrentBlock is { } stopped) return new(null, stopped.Code, stopped.Until);
            if (jobs.Values.Count(job => job.FinishedAt is null) >= options.Value.QueueCapacity)
                return new(null, "QueueFull", now.AddSeconds(10));
            var job = new PreparationJob(Guid.NewGuid(), poiId, language, sourceVersion, "Queued", textReady,
                null, null, now, null, isInteractive);
            jobs.Add(job.Id, job);
            keys[key] = job.Id;
            (isInteractive ? interactive : nearby).AddLast(job.Id);
            available.Release();
            return new(job);
        }
    }

    public PreparationJob? Find(Guid id)
    {
        lock (gate) { Prune(clock.GetUtcNow()); return jobs.GetValueOrDefault(id); }
    }

    public async Task<PreparationJob> TakeAsync(CancellationToken token)
    {
        await available.WaitAsync(token);
        lock (gate)
        {
            var list = interactive.Count > 0 ? interactive : nearby;
            var id = list.First!.Value;
            list.RemoveFirst();
            return jobs[id] = jobs[id] with { Status = "Processing" };
        }
    }

    public void Complete(Guid id, string status, bool textReady, string? errorCode = null, DateTimeOffset? retryAt = null,
        string? failureStage = null)
    {
        lock (gate)
        {
            jobs[id] = jobs[id] with { Status = status, TextReady = textReady, ErrorCode = errorCode,
                RetryAt = retryAt, FinishedAt = clock.GetUtcNow(), FailureStage = failureStage };
            Prune(clock.GetUtcNow());
        }
    }

    public void Block(string errorCode, DateTimeOffset until)
    {
        lock (gate)
            if (block is null || block.Value.Until < until) block = (errorCode, until);
    }

    public PreparationQueueSnapshot Snapshot()
    {
        lock (gate)
        {
            Prune(clock.GetUtcNow());
            var current = jobs.Values.ToArray();
            var stopped = CurrentBlock;
            return new(current.Count(job => job.Status == "Queued"),
                current.Count(job => job.Status == "Processing"),
                current.Count(PreparationQueueSnapshot.IsFailure), current,
                stopped?.Code, stopped?.Until, startedAt, options.Value.JobRetentionMinutes, options.Value.RetainedJobs);
        }
    }

    private void Prune(DateTimeOffset now)
    {
        var finished = jobs.Values.Where(job => job.FinishedAt is not null)
            .OrderByDescending(job => job.FinishedAt).ToArray();
        foreach (var job in finished.Where((job, index) => index >= options.Value.RetainedJobs
                     || job.FinishedAt <= now.AddMinutes(-options.Value.JobRetentionMinutes)))
        {
            jobs.Remove(job.Id);
            var key = (job.PoiId, job.Language, job.SourceVersion);
            if (keys.GetValueOrDefault(key) == job.Id) keys.Remove(key);
        }
    }
}
