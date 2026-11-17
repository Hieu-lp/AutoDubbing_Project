using System.ComponentModel.DataAnnotations;
using AutoDubbing.Data.Contexts;
using AutoDubbing.Data.Entities;
using AutoDubbing.Service.Monitoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoDubbing.API.Controllers;

[ApiController, AllowAnonymous, Route("api")]
public sealed class VisitorActivityController(ActivityService activity, VisitorPresence presence) : ControllerBase
{
    [HttpPost("visitor-heartbeat")]
    public IActionResult Heartbeat(HeartbeatInput input)
    { if (input.GuestId == Guid.Empty) return BadRequest(); presence.Touch(input.GuestId); return NoContent(); }
    [HttpPost("usage-events")]
    public async Task<IActionResult> Record(UsageInput input, CancellationToken token)
    {
        try { await activity.RecordAsync(input, token); return NoContent(); }
        catch (ArgumentException error) { return Problem(statusCode: 400, title: error.Message); }
    }
}
public sealed record HeartbeatInput(Guid GuestId);

[ApiController, Authorize, Route("api/admin")]
public sealed class AdminMonitoringController(AppDbContext db) : ControllerBase
{
    [HttpGet("usage-events")]
    public async Task<IActionResult> Usage([FromQuery] UsageHistoryQuery request, CancellationToken token = default)
    {
        Response.Headers.CacheControl = "no-store";
        var rows = await FilterUsage(request, out _, out _)
            .OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Id)
            .Take(request.Take).Select(item => new { item.Id, PoiId = item.PointOfInterestId,
                PoiName = item.PointOfInterest == null ? null : item.PointOfInterest.Name, item.Language,
                item.EventType, item.RouteId, RouteName = item.Route == null ? null : item.Route.Name,
                OccurredAtUtc = DateTime.SpecifyKind(item.OccurredAt, DateTimeKind.Utc), item.EventId }).ToArrayAsync(token);
        return Ok(rows);
    }

    [HttpGet("usage-events/statistics")]
    public async Task<IActionResult> UsageStatistics([FromQuery] UsageHistoryQuery request, CancellationToken token = default)
    {
        Response.Headers.CacheControl = "no-store";
        var listens = FilterUsage(request, out var since, out var until).Where(item => item.PointOfInterestId != null
            && (item.EventType == "auto_play" || item.EventType == "manual_play"));
        var counts = await listens.GroupBy(item => new {
            PoiId = item.PointOfInterestId!.Value,
            PoiName = item.PointOfInterest == null ? "POI không còn tồn tại" : item.PointOfInterest.Name
        }).Select(group => new {
            group.Key.PoiId, group.Key.PoiName, Listens = group.Count(),
            Listeners = group.Where(item => item.GuestId != null).Select(item => item.GuestId).Distinct().Count()
        }).ToArrayAsync(token);
        var hourly = await listens.GroupBy(item => new {
            PoiId = item.PointOfInterestId!.Value, Hour = item.OccurredAt.AddHours(7).Hour
        }).Select(group => new { group.Key.PoiId, group.Key.Hour, Listens = group.Count() }).ToArrayAsync(token);
        var listeners = await listens.Where(item => item.GuestId != null)
            .Select(item => item.GuestId).Distinct().CountAsync(token);
        var byPoi = counts.Select(poi => {
            var peak = hourly.Where(hour => hour.PoiId == poi.PoiId)
                .OrderByDescending(hour => hour.Listens).ThenBy(hour => hour.Hour).FirstOrDefault();
            return new { poi.PoiId, poi.PoiName, poi.Listens, poi.Listeners,
                PeakHour = peak?.Hour, PeakListens = peak?.Listens ?? 0 };
        }).OrderByDescending(poi => poi.Listens).ThenByDescending(poi => poi.Listeners).ThenBy(poi => poi.PoiId).ToArray();
        var byHour = Enumerable.Range(0, 24).Select(hour => {
            var rows = hourly.Where(item => item.Hour == hour).ToArray();
            var top = rows.OrderByDescending(item => item.Listens).ThenBy(item => item.PoiId).FirstOrDefault();
            return new { Hour = hour, Listens = rows.Sum(item => item.Listens), TopPoiId = top?.PoiId,
                TopPoiName = counts.FirstOrDefault(poi => poi.PoiId == top?.PoiId)?.PoiName,
                TopPoiListens = top?.Listens ?? 0 };
        }).ToArray();
        return Ok(new {
            SinceUtc = DateTime.SpecifyKind(since, DateTimeKind.Utc),
            UntilUtc = DateTime.SpecifyKind(until, DateTimeKind.Utc), TimeZone = "Asia/Ho_Chi_Minh",
            TotalListens = counts.Sum(poi => poi.Listens), UniqueListeners = listeners,
            TopPoi = byPoi.FirstOrDefault(),
            PeakHour = byHour.Where(hour => hour.Listens > 0).OrderByDescending(hour => hour.Listens)
                .ThenBy(hour => hour.Hour).FirstOrDefault(),
            ByPoi = byPoi, ByHour = byHour
        });
    }

    private IQueryable<UsageEvent> FilterUsage(UsageHistoryQuery request, out DateTime since, out DateTime until)
    {
        until = request.UntilUtc?.UtcDateTime ?? DateTime.UtcNow;
        since = request.SinceUtc?.UtcDateTime ?? until.AddDays(-1);
        var start = since; var end = until;
        var query = db.UsageEvents.AsNoTracking().Where(item => item.OccurredAt >= start && item.OccurredAt < end);
        if (request.PoiId is int poiId) query = query.Where(item => item.PointOfInterestId == poiId);
        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            var language = request.Language.Trim().ToLowerInvariant();
            query = query.Where(item => item.Language == language);
        }
        if (request.EventType is string eventType) query = query.Where(item => item.EventType == eventType);
        var startHour = request.StartHour; var endHour = request.EndHour;
        return startHour < endHour
            ? query.Where(item => item.OccurredAt.AddHours(7).Hour >= startHour && item.OccurredAt.AddHours(7).Hour < endHour)
            : query.Where(item => item.OccurredAt.AddHours(7).Hour >= startHour || item.OccurredAt.AddHours(7).Hour < endHour);
    }
    [HttpGet("logs")]
    public async Task<IActionResult> Logs([FromQuery] int? poiId, [FromQuery] string? language,
        [FromQuery] string? errorCode, [FromQuery] string? level, [FromQuery] DateTime? sinceUtc,
        [FromQuery, Range(1, 200)] int take = 100, CancellationToken token = default)
    {
        var since = sinceUtc ?? DateTime.UtcNow.AddDays(-1);
        if (since < DateTime.UtcNow.AddDays(-31)) return BadRequest(new ProblemDetails { Title = "Chỉ truy vấn tối đa 31 ngày." });
        var rows = await db.SystemLogs.AsNoTracking().Where(item => item.OccurredAt >= since
            && (poiId == null || item.PointOfInterestId == poiId) && (language == null || item.Language == language)
            && (errorCode == null || item.ErrorCode == errorCode) && (level == null || item.Level == level))
            .OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Id).Take(take).ToArrayAsync(token);
        return Ok(rows.Select(item => new { item.Id, Component = item.Component == "ContentPreparation" ? item.Component : "SQL",
            Level = item.Level is "error" or "info" or "warning" ? item.Level : "unknown",
            Message = item.ErrorCode == "Completed" ? "Đã hoàn tất nội dung và audio." : PipelineErrors.Explain(item.ErrorCode).Message,
            ErrorCode = item.ErrorCode == "Completed" ? "Completed" : PipelineErrors.Explain(item.ErrorCode).Code,
            PoiId = item.PointOfInterestId, item.Language, item.JobId, Stage = PipelineErrors.Stage(item.Stage),
            OccurredAtUtc = DateTime.SpecifyKind(item.OccurredAt, DateTimeKind.Utc) }));
    }
}

public sealed class UsageHistoryQuery : IValidatableObject
{
    [Range(1, int.MaxValue)] public int? PoiId { get; init; }
    [StringLength(35)] public string? Language { get; init; }
    [RegularExpression("^(auto_play|manual_play|route_selection)$")] public string? EventType { get; init; }
    public DateTimeOffset? SinceUtc { get; init; }
    public DateTimeOffset? UntilUtc { get; init; }
    [Range(0, 23)] public int StartHour { get; init; }
    [Range(0, 24)] public int EndHour { get; init; } = 24;
    [Range(1, 200)] public int Take { get; init; } = 100;

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var until = UntilUtc ?? now;
        var since = SinceUtc ?? until.AddDays(-1);
        if (since >= until)
            yield return new("Thời gian bắt đầu phải trước thời gian kết thúc.", [nameof(SinceUtc), nameof(UntilUtc)]);
        if (since < now.AddDays(-31) || until - since > TimeSpan.FromDays(31))
            yield return new("Chỉ truy vấn dữ liệu trong 31 ngày gần nhất.", [nameof(SinceUtc), nameof(UntilUtc)]);
        if (StartHour == EndHour)
            yield return new("Giờ bắt đầu và kết thúc phải khác nhau; chọn 00–24 để xem cả ngày.", [nameof(StartHour), nameof(EndHour)]);
    }
}
