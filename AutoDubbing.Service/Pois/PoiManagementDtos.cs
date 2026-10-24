using System.ComponentModel.DataAnnotations;
using AutoDubbing.Data.Entities;

namespace AutoDubbing.Service.Pois;

/// <summary>Input used to create or update an administrator-managed POI.</summary>
public sealed record PoiManagementRequest : IValidatableObject
{
    [Required, StringLength(200, MinimumLength = 1)]
    public required string Name { get; init; }

    [Range(-90, 90)]
    public double Latitude { get; init; }

    [Range(-180, 180)]
    public double Longitude { get; init; }

    [Range(1, 10000)]
    public double ProximityRadius { get; init; } = 50;

    [StringLength(2048)]
    public string? ThumbnailUrl { get; init; }

    [Required, StringLength(10000, MinimumLength = 1)]
    public required string OriginalDescription { get; init; }

    [Required, StringLength(35, MinimumLength = 2)]
    public required string OriginalLanguage { get; init; }

    [Required, RegularExpression("^(Published|Hidden)$")]
    public required string Status { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!LanguageTags.TryNormalize(OriginalLanguage, out _))
        {
            yield return new ValidationResult("Mã ngôn ngữ gốc không hợp lệ.",
                [nameof(OriginalLanguage)]);
        }
    }
}

/// <summary>Administrator view of a POI and its publication status.</summary>
public sealed record PoiManagementResponse(
    int Id,
    string Name,
    double Latitude,
    double Longitude,
    double ProximityRadius,
    string? ThumbnailUrl,
    string OriginalDescription,
    string OriginalLanguage,
    string Status)
{
    public static PoiManagementResponse FromEntity(PointOfInterest poi) => new(
        poi.Id, poi.Name, poi.Latitude, poi.Longitude, poi.ProximityRadius,
        poi.ThumbnailUrl, poi.OriginalDescription, poi.OriginalLanguage, poi.Status);
}

/// <summary>Outcome of deleting an administrator-managed POI.</summary>
public enum PoiDeleteResult
{
    Deleted,
    NotFound,
    HasDependents
}
