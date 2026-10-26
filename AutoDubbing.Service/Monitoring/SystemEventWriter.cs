using AutoDubbing.Data.Contexts;
using AutoDubbing.Data.Entities;
using AutoDubbing.Service.ContentPreparation;
using Microsoft.EntityFrameworkCore;

namespace AutoDubbing.Service.Monitoring;

public sealed class SystemEventWriter(AppDbContext db, TimeProvider clock)
{
    public async Task WriteJobAsync(PreparationJob job, CancellationToken token)
    {
        if (job.FinishedAt is null || await db.SystemLogs.AnyAsync(log => log.JobId == job.Id, token)) return;
        var success = job.Status == "Ready";
        var safe = PipelineErrors.Explain(job.ErrorCode);
        db.SystemLogs.Add(new SystemLog { Component = "ContentPreparation", Level = success ? "info" : "error",
            Message = success ? "Đã hoàn tất nội dung và audio." : safe.Message,
            ErrorCode = success ? "Completed" : safe.Code, Stage = PipelineErrors.Stage(job.FailureStage),
            PointOfInterestId = job.PoiId, Language = job.Language, JobId = job.Id,
            OccurredAt = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync(token);
    }
}
