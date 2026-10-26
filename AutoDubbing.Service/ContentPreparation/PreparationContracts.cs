using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using AutoDubbing.Data.Entities;
using AutoDubbing.Service.Pois;

namespace AutoDubbing.Service.ContentPreparation;

public sealed class ContentPreparationOptions
{
    public const string SectionName = "ContentPreparation";
    public bool AcceptNewGeneration { get; set; } = true;
    [Range(1, 1000)] public double PreparationRadiusMeters { get; set; } = 500;
    [Range(1, 500)] public double PlaybackRadiusMeters { get; set; } = 50;
    [Range(1, 30)] public double DwellSeconds { get; set; } = 3;
    [Range(1, 256)] public int QueueCapacity { get; set; } = 32;
    [Range(1, 4096)] public int RetainedJobs { get; set; } = 256;
    [Range(1, 120)] public int JobRetentionMinutes { get; set; } = 30;
    [Range(1, 60)] public int FailureCooldownMinutes { get; set; } = 5;
    [Range(1, 60)] public int DailyQuotaCooldownMinutes { get; set; } = 30;
    [Range(30, 900)] public int JobTimeoutSeconds { get; set; } = 600;
    [Range(1, 120)] public int ClientRequestsPerMinute { get; set; } = 8;
    [Range(1, 600)] public int GlobalRequestsPerMinute { get; set; } = 30;
}

public static class SourceVersion
{
    // Include the POI name because it is also an input to the translation prompt.
    public static string For(PointOfInterest poi) => poi.SourceVersion;
}

/// <summary>Current state of one requested language. Fallback content is explicitly identified.</summary>
public sealed record PreparationItemResponse(int PoiId, string Language, string SourceVersion,
    string Status, bool TextReady, bool AudioReady, Guid? JobId = null, string? StatusUrl = null,
    string? ErrorCode = null, DateTimeOffset? RetryAt = null, PoiContentDto? Content = null,
    double? DistanceMeters = null)
{
    public bool RequiresOperatorAction => RetryAt == DateTimeOffset.MaxValue;
}

/// <summary>Source-language preparation results, ordered by distance from the supplied coordinates.</summary>
public sealed record NearbyPreparationResponse(double PreparationRadiusMeters,
    IReadOnlyList<PreparationItemResponse> Items);

/// <summary>Frontend proximity settings, independent of the per-POI database radius.</summary>
public sealed record ProximitySettingsResponse(double PreparationRadiusMeters, double PlaybackRadiusMeters,
    double DwellSeconds, int PollAfterSeconds = 3);

public interface IContentPreparationService
{
    Task<PreparationItemResponse?> PrepareAsync(int poiId, string language, CancellationToken token);
    Task<NearbyPreparationResponse> PrepareNearbyAsync(double latitude, double longitude, CancellationToken token);
    Task<PreparationItemResponse?> StatusAsync(Guid jobId, CancellationToken token);
}

public sealed record PreparationJob(Guid Id, int PoiId, string Language, string SourceVersion,
    string Status, bool TextReady, string? ErrorCode, DateTimeOffset? RetryAt,
    DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt, bool Interactive, string? FailureStage = null);

public sealed record QueueAdmission(PreparationJob? Job, string? ErrorCode = null,
    DateTimeOffset? RetryAt = null);

public sealed record PreparationQueueSnapshot(int Queued, int Processing, int Failed,
    IReadOnlyList<PreparationJob> Jobs, string? BlockCode, DateTimeOffset? BlockUntil,
    DateTimeOffset StartedAt, int RetentionMinutes, int RetainedJobs)
{
    public static bool IsFailure(PreparationJob job) =>
        job.FinishedAt is not null && (job.Status is "Failed" or "Unavailable" or "QuotaExceeded");
}

public interface IContentPreparationQueue
{
    QueueAdmission Enqueue(int poiId, string language, string sourceVersion, bool textReady, bool interactive);
    PreparationJob? Find(Guid id);
    Task<PreparationJob> TakeAsync(CancellationToken token);
    void Complete(Guid id, string status, bool textReady, string? errorCode = null, DateTimeOffset? retryAt = null,
        string? failureStage = null);
    void Block(string errorCode, DateTimeOffset until);
    PreparationQueueSnapshot Snapshot();
    (string Code, DateTimeOffset Until)? CurrentBlock { get; }
}
