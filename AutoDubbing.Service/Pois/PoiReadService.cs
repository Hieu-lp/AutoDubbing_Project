using AutoDubbing.Data.Entities;
using AutoDubbing.Data.Repositories;
using AutoDubbing.Service.Geography;
using AutoDubbing.Service.ContentGeneration;

namespace AutoDubbing.Service.Pois;

public sealed class PoiReadService(IPoiReadRepository repository) : IPoiReadService
{
    public async Task<IReadOnlyList<PoiSummaryDto>> ListAsync(CancellationToken cancellationToken) =>
        (await repository.ListPublishedAsync(cancellationToken)).Select(poi => new PoiSummaryDto(
            poi.Id, poi.Name, poi.Latitude, poi.Longitude, poi.ProximityRadius,
            poi.ThumbnailUrl, poi.OriginalLanguage)).ToArray();

    public async Task<PoiDetailDto?> FindAsync(int id, CancellationToken cancellationToken)
    {
        var poi = await repository.FindPublishedAsync(id, cancellationToken);
        return poi is null ? null : new PoiDetailDto(poi.Id, poi.Name, poi.Latitude, poi.Longitude,
            poi.ProximityRadius, poi.ThumbnailUrl, poi.OriginalLanguage, poi.OriginalDescription);
    }

    public async Task<IReadOnlyList<NearbyPoiDto>> NearbyAsync(double latitude, double longitude,
        double radiusMeters, CancellationToken cancellationToken)
    {
        if (!GeoMath.IsLatitude(latitude) || !GeoMath.IsLongitude(longitude)
            || !double.IsFinite(radiusMeters) || radiusMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters));
        }

        var pois = await repository.ListPublishedAsync(cancellationToken);
        var nearby = new List<NearbyPoiDto>();
        foreach (var poi in pois)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var distance = GeoMath.DistanceMeters(latitude, longitude, poi.Latitude, poi.Longitude);
            if (distance <= radiusMeters)
            {
                nearby.Add(new NearbyPoiDto(poi.Id, poi.Name, poi.Latitude, poi.Longitude,
                    poi.ProximityRadius, poi.ThumbnailUrl, poi.OriginalLanguage, distance));
            }
        }
        return nearby.OrderBy(poi => poi.DistanceMeters).ThenBy(poi => poi.Id).ToArray();
    }

    public async Task<PoiContentDto?> ContentAsync(int id, string language, CancellationToken cancellationToken)
        => await ReadContentAsync(id, language, false, cancellationToken);

    public async Task<PoiContentDto?> ContentForAdminAsync(int id, string language, CancellationToken cancellationToken)
        => await ReadContentAsync(id, language, true, cancellationToken);

    private async Task<PoiContentDto?> ReadContentAsync(int id, string language, bool admin, CancellationToken cancellationToken)
    {
        if (!LanguageTags.TryNormalize(language, out var requestedLanguage))
        {
            throw new ArgumentException("Mã ngôn ngữ không hợp lệ.", nameof(language));
        }
        var poi = admin ? await repository.FindForAdminAsync(id, cancellationToken) : await repository.FindPublishedAsync(id, cancellationToken);
        if (poi is null) { return null; }
        if (!LanguageTags.TryNormalize(poi.OriginalLanguage, out var originalLanguage))
        {
            throw new InvalidOperationException("POI có mã ngôn ngữ gốc không hợp lệ.");
        }

        PoiTranslation? translation = null;
        if (requestedLanguage != originalLanguage)
        {
            translation = await repository.FindReadyTranslationAsync(id, requestedLanguage, cancellationToken);
            if (translation?.SourceVersion is { } version && version != poi.SourceVersion) translation = null;
        }
        var actualLanguage = translation is null ? originalLanguage : requestedLanguage;
        var text = translation?.Text ?? poi.OriginalDescription;
        var audio = await repository.FindReadyAudioAsync(id, actualLanguage, cancellationToken);
        PoiAudioDto? audioDto = null;
        var textHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
        if (audio is not null && Uri.TryCreate(audio.FilePath, UriKind.Absolute, out var audioUri)
            && (audio.SourceVersion is null || audio.SourceVersion == poi.SourceVersion)
            && (audio.TextHash is null || audio.TextHash == textHash)
            && audioUri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(audioUri.Host)
            && string.IsNullOrEmpty(audioUri.UserInfo)
            && (!GeneratedAudioPaths.IsManaged(audio.FilePath)
                || GeneratedAudioPaths.Matches(audio.FilePath, id, actualLanguage, text)))
        {
            audioDto = new PoiAudioDto(audio.Id, actualLanguage, audio.FilePath);
        }
        return new PoiContentDto(id, requestedLanguage, actualLanguage, actualLanguage != requestedLanguage,
            text, audioDto);
    }
}
