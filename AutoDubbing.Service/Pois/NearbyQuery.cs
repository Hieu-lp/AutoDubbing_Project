using System.ComponentModel.DataAnnotations;
using AutoDubbing.Service.Validation;

namespace AutoDubbing.Service.Pois;

public sealed class NearbyQuery
{
    [Required(ErrorMessage = "Thiếu latitude.")]
    [FiniteRange(-90, 90)]
    public double? Latitude { get; init; }

    [Required(ErrorMessage = "Thiếu longitude.")]
    [FiniteRange(-180, 180)]
    public double? Longitude { get; init; }

    [Required(ErrorMessage = "Thiếu radiusMeters.")]
    [PositiveFinite]
    public double? RadiusMeters { get; init; }
}
