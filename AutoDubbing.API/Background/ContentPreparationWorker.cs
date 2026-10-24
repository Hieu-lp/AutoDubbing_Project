using AutoDubbing.Service.ContentGeneration;
using AutoDubbing.Service.ContentPreparation;
using Microsoft.Extensions.Options;

namespace AutoDubbing.API.Background;

// Exactly one consumer. A request disconnect does not cancel an accepted shared job.
public sealed class ContentPreparationWorker(IContentPreparationQueue queue, IServiceScopeFactory scopes,
    IOptions<ContentPreparationOptions> options, TimeProvider clock, ILogger<ContentPreparationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var job = await queue.TakeAsync(stoppingToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.JobTimeoutSeconds));
                try
                {
                    if (queue.CurrentBlock is { } block)
                    {
                        queue.Complete(job.Id, ContentPreparationService.IsQuota(block.Code) ? "QuotaExceeded" : "Unavailable",
                            job.TextReady, block.Code, block.Until);
                        continue;
                    }
                    await using var scope = scopes.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<IContentGenerationService>();
                    var batch = await service.GenerateAsync(job.PoiId, [job.Language], 1, timeout.Token, job.SourceVersion);
                    if (batch is null) { queue.Complete(job.Id, "Unavailable", false, "PoiUnavailable"); continue; }
                    var result = batch.Results.Single();
                    if (result.AudioReady) { queue.Complete(job.Id, "Ready", result.TextReady); continue; }
                    var code = result.ErrorCode ?? "GenerationFailed";
                    var retryAt = clock.GetUtcNow().AddMinutes(options.Value.FailureCooldownMinutes);
                    var status = "Failed";
                    if (result.Status == "PausedQuota")
                    {
                        code = result.FailureDetails?.QuotaKind switch
                        {
                            "ZeroLimit" => "QuotaUnavailable",
                            "DailyLimit" => "DailyQuotaExceeded",
                            _ => code
                        };
                        status = "QuotaExceeded";
                        retryAt = code == "QuotaUnavailable" ? DateTimeOffset.MaxValue
                            : clock.GetUtcNow().AddSeconds(Math.Max(
                                code == "DailyQuotaExceeded" ? options.Value.DailyQuotaCooldownMinutes * 60 : 60,
                                Math.Min(result.FailureDetails?.RetryAfterSeconds ?? 60, 86400)));
                        queue.Block(code, retryAt);
                    }
                    else if (code is "GeminiUnauthorized" or "GeminiForbidden" or "GeminiModelUnavailable")
                    {
                        status = "Unavailable";
                        retryAt = DateTimeOffset.MaxValue;
                        queue.Block(code, retryAt);
                    }
                    else if (code == "SourceChanged") status = "Superseded";
                    else if (code.StartsWith("Supabase", StringComparison.Ordinal) || code == "PersistenceUnavailable")
                        queue.Block(code, retryAt);
                    queue.Complete(job.Id, status, result.TextReady, code, retryAt);
                }
                catch (OperationCanceledException)
                {
                    queue.Complete(job.Id, "Failed", job.TextReady,
                        stoppingToken.IsCancellationRequested ? "WorkerStopped" : "JobTimeout",
                        clock.GetUtcNow().AddMinutes(options.Value.FailureCooldownMinutes));
                }
                catch (GenerationException error)
                {
                    queue.Complete(job.Id, error.Code == "SourceChanged" ? "Superseded" : "Unavailable",
                        job.TextReady, error.Code, clock.GetUtcNow().AddMinutes(options.Value.FailureCooldownMinutes));
                }
                catch (Exception error)
                {
                    // Never log the exception body, connection strings, provider responses or credentials.
                    logger.LogWarning("Preparation {JobId} failed with exception type {ExceptionType}", job.Id, error.GetType().Name);
                    var until = clock.GetUtcNow().AddMinutes(options.Value.FailureCooldownMinutes);
                    queue.Complete(job.Id, "Failed", job.TextReady, "PreparationUnavailable", until);
                    queue.Block("PreparationUnavailable", until);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
