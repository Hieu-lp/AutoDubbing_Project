using AutoDubbing.Service.Geography;
using Xunit;

namespace AutoDubbing.Tests;

public sealed class GeoMathTests
{
    [Theory]
    [InlineData(10, 106, 10, 106)]
    [InlineData(0, -180, 0, 180)]
    [InlineData(90, 50, 90, -120)]
    public void Same_location_has_zero_distance(double latA, double lonA, double latB, double lonB) =>
        Assert.Equal(0, GeoMath.DistanceMeters(latA, lonA, latB, lonB));

    [Fact]
    public void One_equatorial_degree_is_about_111195_meters_and_distance_is_symmetric()
    {
        var distance = GeoMath.DistanceMeters(0, 0, 0, 1);
        Assert.InRange(distance, 111_194.9, 111_195.0);
        Assert.Equal(distance, GeoMath.DistanceMeters(0, 1, 0, 0));
    }

    [Fact]
    public void Antipodal_and_date_line_distances_remain_finite()
    {
        Assert.Equal(Math.PI * GeoMath.EarthRadiusMeters, GeoMath.DistanceMeters(0, 0, 0, 180), 6);
        Assert.InRange(GeoMath.DistanceMeters(0, 179.9, 0, -179.9), 22_238, 22_240);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    public void Invalid_coordinates_are_rejected(double latitude, double longitude) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GeoMath.DistanceMeters(latitude, longitude, 0, 0));
}
