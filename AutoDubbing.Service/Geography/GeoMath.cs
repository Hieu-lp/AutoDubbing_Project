namespace AutoDubbing.Service.Geography;

public static class GeoMath
{
    public const double EarthRadiusMeters = 6_371_000;

    public static bool IsLatitude(double value) => double.IsFinite(value) && value is >= -90 and <= 90;
    public static bool IsLongitude(double value) => double.IsFinite(value) && value is >= -180 and <= 180;

    public static double DistanceMeters(double latitudeA, double longitudeA, double latitudeB, double longitudeB)
    {
        if (!IsLatitude(latitudeA) || !IsLongitude(longitudeA)
            || !IsLatitude(latitudeB) || !IsLongitude(longitudeB))
        {
            throw new ArgumentOutOfRangeException(nameof(latitudeA), "Tọa độ phải hữu hạn và đúng phạm vi.");
        }

        if (latitudeA == latitudeB && (longitudeA == longitudeB
            || Math.Abs(longitudeA - longitudeB) == 360 || Math.Abs(latitudeA) == 90))
        {
            return 0;
        }

        var latitudeDelta = ToRadians(latitudeB - latitudeA);
        var longitudeDelta = ToRadians(longitudeB - longitudeA);
        var a = Math.Pow(Math.Sin(latitudeDelta / 2), 2)
            + Math.Cos(ToRadians(latitudeA)) * Math.Cos(ToRadians(latitudeB))
            * Math.Pow(Math.Sin(longitudeDelta / 2), 2);
        a = Math.Clamp(a, 0, 1);
        return 2 * EarthRadiusMeters * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
