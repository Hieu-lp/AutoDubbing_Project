using AutoDubbing.Data.Contexts;
using AutoDubbing.Service.ContentGeneration;
using AutoDubbing.Service.ContentPreparation;
using AutoDubbing.Service.Monitoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutoDubbing.API.Controllers;

[ApiController]
[Route("api/admin/dashboard")]
[Authorize]
public sealed class AdminDashboardController(AppDbContext db, IContentPreparationQueue queue,
    IContentGenerationService generation, IOptions<ContentPreparationOptions> options,
    TimeProvider clock, ILogger<AdminDashboardController> logger, VisitorPresence presence)
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
        var isBlocked = !options.Value.AcceptNewGeneration || !generation.IsConfigured || queueState.BlockCode is not null;
        var pipelineHealth = isBlocked ? "warning" : "online";
        var reasons = new List<AdminPipelineReason>();
        if (!options.Value.AcceptNewGeneration)
            reasons.Add(new("GenerationPaused", "Tạo nội dung mới đang được người vận hành tạm dừng.", null, true));
        if (!generation.IsConfigured)
            reasons.Add(new("GenerationNotConfigured", "Cấu hình Gemini hoặc Supabase còn thiếu hoặc không hợp lệ.", null, true));
        if (queueState.BlockCode is { } blockCode)
        {
            var explained = Explain(blockCode);
            reasons.Add(new(explained.Code, "Đang chặn tác vụ mới: " + explained.Message,
                FiniteTime(queueState.BlockUntil), queueState.BlockUntil == DateTimeOffset.MaxValue));
        }
        if (queueState.Failed > 0)
            reasons.Add(new("PastFailures", $"Có {queueState.Failed} lần xử lý không hoàn tất trong lịch sử đang giữ. Riêng lỗi cũ không chặn tác vụ mới.", null, false));
        if (reasons.Count == 0) reasons.Add(new("Ready", "Sẵn sàng nhận yêu cầu mới.", null, false));
        var health = new AdminDashboardHealth("online", "offline", pipelineHealth);
        IReadOnlyList<AdminDashboardError> recentErrors = [];
        IReadOnlyList<AdminDashboardPoiListen> listensByPoi = [];
        IReadOnlyList<AdminDashboardLanguageListen> listensByLanguage = [];
        int? autoPlayCount = null;
        int? manualPlayCount = null;
        AdminDashboardTopRoute? topRoute = null;
        var logsAvailable = false;
        var poiNames = new Dictionary<int, string>();

        try
        {
            if (await db.Database.CanConnectAsync(cancellationToken))
            {
                health = health with { Database = "online" };

                var poiIds = queueState.Jobs.Where(PreparationQueueSnapshot.IsFailure).Select(job => job.PoiId).Distinct().ToArray();
                poiNames = await db.PointsOfInterest.AsNoTracking().Where(poi => poiIds.Contains(poi.Id))
                    .ToDictionaryAsync(poi => poi.Id, poi => poi.Name, cancellationToken);
                // SQL messages are not a sanitized error contract and may contain credentials or stack traces.
                var errorRows = await db.SystemLogs.AsNoTracking()
                    .Where(item => item.OccurredAt >= since && item.Level.ToLower() == "error")
                    .OrderByDescending(item => item.OccurredAt)
                    .Take(5)
                    .ToListAsync(cancellationToken);
                recentErrors = errorRows.Select(item => new AdminDashboardError("Nhật ký SQL",
                    PipelineErrors.Explain(item.ErrorCode).Message, item.OccurredAt,
                    item.PointOfInterestId, item.Language, item.JobId, PipelineErrors.Explain(item.ErrorCode).Code,
                    PipelineErrors.Stage(item.Stage))).ToArray();
                logsAvailable = true;

                // UsageEvent has no visitor/session identity. Only recognized playback event types
                // are counted, so unrelated events are not mislabeled as listens.
                var events = await db.UsageEvents.AsNoTracking()
                    .Where(item => item.OccurredAt >= since)
                    .Select(item => new { item.EventType, item.Language, PoiId = item.PointOfInterestId,
                        PoiName = item.PointOfInterest == null ? "POI không còn tồn tại" : item.PointOfInterest.Name,
                        item.RouteId, RouteName = item.Route == null ? null : item.Route.Name })
                    .ToListAsync(cancellationToken);
                var playback = events.Select(item => new
                    {
                        item.PoiId, item.PoiName, item.Language,
                        Kind = PlaybackKind(item.EventType)
                    })
                    .Where(item => item.Kind is not null && item.PoiId is not null)
                    .ToArray();

                autoPlayCount = playback.Count(item => item.Kind == "auto");
                manualPlayCount = playback.Count(item => item.Kind == "manual");
                var popular = events.Where(item => item.EventType == "route_selection" && item.RouteId is not null)
                    .GroupBy(item => new { item.RouteId, item.RouteName }).OrderByDescending(group => group.Count())
                    .ThenBy(group => group.Key.RouteId).FirstOrDefault();
                if (popular is not null) topRoute = new(popular.Key.RouteId!.Value, popular.Key.RouteName ?? "Lộ trình đã xóa", popular.Count());
                if (playback.Length > 0)
                {
                    autoPlayCount = playback.Count(item => item.Kind == "auto");
                    manualPlayCount = playback.Count(item => item.Kind == "manual");
                    listensByPoi = playback.GroupBy(item => new { item.PoiId, item.PoiName })
                        .OrderByDescending(group => group.Count())
                        .Take(5)
                        .Select(group => new AdminDashboardPoiListen(group.Key.PoiId!.Value, group.Key.PoiName, group.Count()))
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
            logger.LogWarning("Could not read all admin dashboard data; exception type {ExceptionType}", error.GetType().Name);
            health = health with { Database = "offline" };
            recentErrors = [];
            logsAvailable = false;
            listensByPoi = [];
            listensByLanguage = [];
            autoPlayCount = null;
            manualPlayCount = null;
        }

        var ready = queueState.Jobs.Where(job => job.Status == "Ready" && job.FinishedAt is not null)
            .GroupBy(job => (job.PoiId, job.Language, job.SourceVersion))
            .ToDictionary(group => group.Key, group => group.Max(job => job.FinishedAt!.Value));
        var failedJobs = queueState.Jobs.Where(PreparationQueueSnapshot.IsFailure)
            .OrderByDescending(job => job.FinishedAt).Select(job =>
            {
                var explained = Explain(job.ErrorCode);
                var recoveredAt = ready.GetValueOrDefault((job.PoiId, job.Language, job.SourceVersion));
                return new AdminFailedJob(job.Id, job.PoiId, poiNames.GetValueOrDefault(job.PoiId),
                    job.Language, job.Status, job.FinishedAt, SafeStage(job.FailureStage), explained.Code,
                    explained.Message, FiniteTime(job.RetryAt), job.RetryAt == DateTimeOffset.MaxValue,
                    recoveredAt >= job.FinishedAt ? recoveredAt : null);
            }).ToArray();
        var summary = new AdminDashboardSummary(now, health,
            new AdminDashboardPipeline(queueState.Queued, queueState.Processing, queueState.Failed,
                null, null, recentErrors, logsAvailable, isBlocked, reasons, failedJobs,
                new(queueState.StartedAt, queueState.RetentionMinutes, queueState.RetainedJobs, true)),
            new AdminDashboardUsage(presence.Count, autoPlayCount, manualPlayCount, listensByPoi,
                listensByLanguage, topRoute));
        return Ok(summary);
    }

    private static DateTimeOffset? FiniteTime(DateTimeOffset? value) => value == DateTimeOffset.MaxValue ? null : value;
    private static string? SafeStage(string? value) => value is "translation" or "tts" or "upload" or "persistence" or "cache" ? value : null;
    private static (string Code, string Message) Explain(string? code) =>
        AutoDubbing.Service.Monitoring.PipelineErrors.Explain(code);

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
    double? ErrorRatePercent, double? AvgResponseMs, IReadOnlyList<AdminDashboardError> RecentErrors,
    bool LogsAvailable, bool IsBlocked, IReadOnlyList<AdminPipelineReason> Reasons,
    IReadOnlyList<AdminFailedJob> FailedJobs, AdminJobHistory History);

public sealed record AdminPipelineReason(string Code, string Message, DateTimeOffset? RetryAtUtc, bool RequiresOperatorAction);
public sealed record AdminJobHistory(DateTimeOffset StartedAtUtc, int RetentionMinutes, int MaxCompletedJobs, bool LostOnRestart);
public sealed record AdminFailedJob(Guid JobId, int PoiId, string? PoiName, string Language, string Status,
    DateTimeOffset? FailedAtUtc, string? Stage, string ErrorCode, string Message,
    DateTimeOffset? RetryAtUtc, bool RequiresOperatorAction, DateTimeOffset? RecoveredAtUtc);

public sealed record AdminDashboardError(string Component, string Message, DateTime OccurredAtUtc,
    int? PoiId = null, string? Language = null, Guid? JobId = null, string? ErrorCode = null, string? Stage = null);

public sealed record AdminDashboardUsage(int? ActiveUsers, int? AutoPlayCount, int? ManualPlayCount,
    IReadOnlyList<AdminDashboardPoiListen> ListensByPoi,
    IReadOnlyList<AdminDashboardLanguageListen> ListensByLanguage, AdminDashboardTopRoute? TopRoute,
    int ActiveWindowSeconds = VisitorPresence.WindowSeconds, bool ActiveUsersLostOnRestart = true);

public sealed record AdminDashboardPoiListen(int PoiId, string Name, int Count);

public sealed record AdminDashboardLanguageListen(string Language, int Count);

public sealed record AdminDashboardTopRoute(int Id, string Name, int Selections);
