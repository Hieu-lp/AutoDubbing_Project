using AutoDubbing.Data.Entities;
using AutoDubbing.Data.Repositories;
using AutoDubbing.Service.ContentGeneration;
using AutoDubbing.Service.Geography;
using AutoDubbing.Service.Languages;
using AutoDubbing.Service.Pois;
using Microsoft.Extensions.Options;

namespace AutoDubbing.Service.ContentPreparation;

public sealed class ContentPreparationService(IPoiReadRepository read, IPoiReadService content,
    IContentGenerationService generation, IContentPreparationQueue queue, IOptions<ContentPreparationOptions> options)
    : IContentPreparationService
{
    public async Task<PreparationItemResponse?> PrepareAsync(int poiId, string language, CancellationToken token)
    {
        var target = LanguageCatalog.Find(language) ?? throw new ArgumentException("Invalid language.", nameof(language));
        var poi = await read.FindPublishedAsync(poiId, token);
        return poi is null ? null : await PreparePoiAsync(poi, target.Code, true, token);
    }

    public async Task<NearbyPreparationResponse> PrepareNearbyAsync(double latitude, double longitude, CancellationToken token)
    {
        if (!GeoMath.IsLatitude(latitude) || !GeoMath.IsLongitude(longitude)) throw new ArgumentOutOfRangeException(nameof(latitude));
        var candidates = (await read.ListPublishedAsync(token))
            .Select(poi => (Poi: poi, Distance: GeoMath.DistanceMeters(latitude, longitude, poi.Latitude, poi.Longitude)))
            .Where(item => item.Distance <= options.Value.PreparationRadiusMeters)
            .OrderBy(item => item.Distance).ThenBy(item => item.Poi.Id);
        var items = new List<PreparationItemResponse>();
        foreach (var item in candidates)
        {
            token.ThrowIfCancellationRequested();
            items.Add((await PreparePoiAsync(item.Poi, item.Poi.OriginalLanguage, false, token))
                with { DistanceMeters = item.Distance });
        }
        return new(options.Value.PreparationRadiusMeters, items);
    }

    private async Task<PreparationItemResponse> PreparePoiAsync(PointOfInterest poi, string language, bool interactive,
        CancellationToken token)
    {
        var version = SourceVersion.For(poi);
        if (poi.OriginalLanguage != "vi" || string.IsNullOrWhiteSpace(poi.OriginalDescription)
            || poi.OriginalDescription.Length > 6000)
            return new(poi.Id, language, version, "Unavailable", false, false, ErrorCode: "UnsupportedSourceContent");
        var saved = await content.ContentAsync(poi.Id, language, token);
        var textReady = saved is { IsFallback: false } && !string.IsNullOrWhiteSpace(saved.Text);
        if (textReady && saved!.Audio is not null)
            return new(poi.Id, language, version, "Ready", true, true, Content: saved);
        if (!options.Value.AcceptNewGeneration || !generation.IsConfigured)
            return new(poi.Id, language, version, "Unavailable", textReady, false,
                ErrorCode: options.Value.AcceptNewGeneration ? "GenerationNotConfigured" : "GenerationPaused", Content: saved);
        var admission = queue.Enqueue(poi.Id, language, version, textReady, interactive);
        if (admission.Job is { } job) return Response(job, saved);
        return new(poi.Id, language, version, admission.ErrorCode == "QueueFull" ? "QueueFull"
                : IsQuota(admission.ErrorCode) ? "QuotaExceeded" : "Unavailable", textReady, false,
            ErrorCode: admission.ErrorCode, RetryAt: admission.RetryAt, Content: saved);
    }

    public async Task<PreparationItemResponse?> StatusAsync(Guid jobId, CancellationToken token)
    {
        var job = queue.Find(jobId);
        if (job is null) return null;
        var poi = await read.FindPublishedAsync(job.PoiId, token);
        if (poi is null) return null;
        if (SourceVersion.For(poi) != job.SourceVersion)
            return Response(job with { Status = "Superseded", TextReady = false, ErrorCode = "SourceChanged", RetryAt = null }, null);
        var saved = await content.ContentAsync(job.PoiId, job.Language, token);
        if (saved is { IsFallback: false, Audio: not null } && !string.IsNullOrWhiteSpace(saved.Text))
            return Response(job with { Status = "Ready", TextReady = true, ErrorCode = null, RetryAt = null }, saved);
        // A previously completed job can outlive the SQL content it describes.
        if (job.Status == "Ready") job = job with { Status = "Missing", ErrorCode = "ContentMissing" };
        return Response(job, saved);
    }

    private static PreparationItemResponse Response(PreparationJob job, PoiContentDto? saved) =>
        new(job.PoiId, job.Language, job.SourceVersion, job.Status,
            saved is { IsFallback: false } && !string.IsNullOrWhiteSpace(saved.Text),
            job.Status == "Ready", job.Id, $"/api/content-preparations/{job.Id}", job.ErrorCode, job.RetryAt, saved);

    public static bool IsQuota(string? code) => code is "QuotaExceeded" or "DailyQuotaExceeded" or "QuotaUnavailable" or "SupabaseQuotaExceeded";
}
