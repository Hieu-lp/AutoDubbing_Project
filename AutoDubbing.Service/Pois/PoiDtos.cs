namespace AutoDubbing.Service.Pois;

public sealed record PoiSummaryDto(int Id, string Name, double Latitude, double Longitude,
    double ProximityRadius, string? ThumbnailUrl, string OriginalLanguage);

public sealed record PoiDetailDto(int Id, string Name, double Latitude, double Longitude,
    double ProximityRadius, string? ThumbnailUrl, string OriginalLanguage, string OriginalDescription);

public sealed record NearbyPoiDto(int Id, string Name, double Latitude, double Longitude,
    double ProximityRadius, string? ThumbnailUrl, string OriginalLanguage, double DistanceMeters);

public sealed record PoiAudioDto(int Id, string Language, string Url);

public sealed record PoiContentDto(int PoiId, string RequestedLanguage, string ActualLanguage,
    bool IsFallback, string Text, PoiAudioDto? Audio);
