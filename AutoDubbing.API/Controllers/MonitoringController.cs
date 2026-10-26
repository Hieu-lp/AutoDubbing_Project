using System.ComponentModel.DataAnnotations;
using AutoDubbing.Data.Contexts;
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
    public async Task<IActionResult> Usage([FromQuery] int? poiId, [FromQuery] string? language,
        [FromQuery] string? eventType, [FromQuery] DateTime? sinceUtc, [FromQuery, Range(1, 200)] int take = 100,
        CancellationToken token = default)
    {
        var since = sinceUtc ?? DateTime.UtcNow.AddDays(-1);
        if (since < DateTime.UtcNow.AddDays(-31)) return BadRequest(new ProblemDetails { Title = "Chỉ truy vấn tối đa 31 ngày." });
        var rows = await db.UsageEvents.AsNoTracking().Where(item => item.OccurredAt >= since
            && (poiId == null || item.PointOfInterestId == poiId) && (language == null || item.Language == language)
            && (eventType == null || item.EventType == eventType)).OrderByDescending(item => item.OccurredAt).ThenByDescending(item => item.Id)
            .Take(take).Select(item => new { item.Id, PoiId = item.PointOfInterestId,
                PoiName = item.PointOfInterest == null ? null : item.PointOfInterest.Name, item.Language,
                item.EventType, item.RouteId, RouteName = item.Route == null ? null : item.Route.Name,
                OccurredAtUtc = DateTime.SpecifyKind(item.OccurredAt, DateTimeKind.Utc), item.EventId }).ToArrayAsync(token);
        return Ok(rows);
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
