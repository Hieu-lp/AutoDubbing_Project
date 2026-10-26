using AutoDubbing.Data.Contexts;
using AutoDubbing.Service.ContentGeneration;
using AutoDubbing.Service.ContentPreparation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoDubbing.API.Controllers;

[ApiController]
[Route("api/admin/dashboard")]
[Authorize]
public sealed class AdminDashboardController(AppDbContext db, IContentPreparationQueue queue,
    IContentGenerationService generation, TimeProvider clock, ILogger<AdminDashboardController> logger)
    : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<AdminDashboardSummary>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminDashboardSummary>> Summary([FromQuery] string range = "24h",
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(range, "24h", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new ProblemDetails { Title = "Khoảng thời gian chưa được hỗ trợ.", Detail = "Hiện chỉ hỗ trợ range=24h." });

        var now = clock.GetUtcNow();
        var since = now.AddHours(-24).UtcDateTime;
        var queueState = queue.Snapshot();
        var block = queue.CurrentBlock;
        var pipelineHealth = !generation.IsConfigured || block is not null || queueState.Failed > 0
            ? "warning" : "online";
        var health = new AdminDashboardHealth("online", "offline", pipelineHealth);
        IReadOnlyList<AdminDashboardError> recentErrors = [];
        IReadOnlyList<AdminDashboardPoiListen> listensByPoi = [];
        IReadOnlyList<AdminDashboardLanguageListen> listensByLanguage = [];
        int? autoPlayCount = null;
        int? manualPlayCount = null;

        try
        {
            if (await db.Database.CanConnectAsync(cancellationToken))
            {
                health = health with { Database = "online" };

                recentErrors = await db.SystemLogs.AsNoTracking()
                    .Where(item => item.OccurredAt >= since && item.Level.ToLower() == "error")
                    .OrderByDescending(item => item.OccurredAt)
                    .Take(5)
                    .Select(item => new AdminDashboardError(item.Component, item.Message, item.OccurredAt))
                    .ToListAsync(cancellationToken);

                // UsageEvent has no visitor/session identity. Only recognized playback event types
                // are counted, so unrelated events are not mislabeled as listens.
                var events = await db.UsageEvents.AsNoTracking()
                    .Where(item => item.OccurredAt >= since)
                    .Select(item => new { item.EventType, item.Language, PoiId = item.PointOfInterestId,
                        PoiName = item.PointOfInterest.Name })
                    .ToListAsync(cancellationToken);
                var playback = events.Select(item => new
                    {
                        item.PoiId, item.PoiName, item.Language,
                        Kind = PlaybackKind(item.EventType)
                    })
                    .Where(item => item.Kind is not null)
                    .ToArray();

                if (playback.Length > 0)
                {
                    autoPlayCount = playback.Count(item => item.Kind == "auto");
                    manualPlayCount = playback.Count(item => item.Kind == "manual");
                    listensByPoi = playback.GroupBy(item => new { item.PoiId, item.PoiName })
                        .OrderByDescending(group => group.Count())
                        .Take(5)
                        .Select(group => new AdminDashboardPoiListen(group.Key.PoiId, group.Key.PoiName, group.Count()))
                        .ToArray();
                    listensByLanguage = playback.GroupBy(item => item.Language)
                        .OrderByDescending(group => group.Count())
                        .Take(8)
                        .Select(group => new AdminDashboardLanguageListen(group.Key, group.Count()))
                        .ToArray();
                }
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogWarning(error, "Could not read all admin dashboard data.");
            health = health with { Database = "offline" };
            recentErrors = [];
            listensByPoi = [];
            listensByLanguage = [];
            autoPlayCount = null;
            manualPlayCount = null;
        }

        var summary = new AdminDashboardSummary(now, health,
            new AdminDashboardPipeline(queueState.Queued, queueState.Processing, queueState.Failed,
                null, null, recentErrors),
            new AdminDashboardUsage(null, autoPlayCount, manualPlayCount, listensByPoi,
                listensByLanguage, null));
        return Ok(summary);
    }

    private static string? PlaybackKind(string eventType)
    {
        var normalized = new string(eventType.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return normalized switch
        {
            "autoplay" or "automaticplay" or "playbackauto" or "playauto" or "tudong" => "auto",
            "manualplay" or "playbackmanual" or "playmanual" or "thutay" => "manual",
            _ => null
        };
    }
}

public sealed record AdminDashboardSummary(DateTimeOffset GeneratedAtUtc, AdminDashboardHealth Health,
    AdminDashboardPipeline Pipeline, AdminDashboardUsage Usage);

public sealed record AdminDashboardHealth(string Api, string Database, string Pipeline);

public sealed record AdminDashboardPipeline(int Queued, int Processing, int Failed,
    double? ErrorRatePercent, double? AvgResponseMs, IReadOnlyList<AdminDashboardError> RecentErrors);

public sealed record AdminDashboardError(string Component, string Message, DateTime OccurredAtUtc);

public sealed record AdminDashboardUsage(int? ActiveUsers, int? AutoPlayCount, int? ManualPlayCount,
    IReadOnlyList<AdminDashboardPoiListen> ListensByPoi,
    IReadOnlyList<AdminDashboardLanguageListen> ListensByLanguage, AdminDashboardTopRoute? TopRoute);

public sealed record AdminDashboardPoiListen(int PoiId, string Name, int Count);

public sealed record AdminDashboardLanguageListen(string Language, int Count);

public sealed record AdminDashboardTopRoute(int Id, string Name, int Selections);
