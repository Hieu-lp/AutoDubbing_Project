namespace AutoDubbing.Service.Pois;

public interface IPoiReadService
{
    Task<PoiContentDto?> ContentForAdminAsync(int id, string language, CancellationToken cancellationToken);
    Task<IReadOnlyList<PoiSummaryDto>> ListAsync(CancellationToken cancellationToken);
    Task<PoiDetailDto?> FindAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyList<NearbyPoiDto>> NearbyAsync(double latitude, double longitude, double radiusMeters,
        CancellationToken cancellationToken);
    Task<PoiContentDto?> ContentAsync(int id, string language, CancellationToken cancellationToken);
}
