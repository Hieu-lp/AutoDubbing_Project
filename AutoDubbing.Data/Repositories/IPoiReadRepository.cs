using AutoDubbing.Data.Entities;

namespace AutoDubbing.Data.Repositories;

public interface IPoiReadRepository
{
    Task<List<PointOfInterest>> ListPublishedAsync(CancellationToken cancellationToken);
    Task<PointOfInterest?> FindPublishedAsync(int id, CancellationToken cancellationToken);
    Task<PointOfInterest?> FindForAdminAsync(int id, CancellationToken cancellationToken) => FindPublishedAsync(id, cancellationToken);
    Task<List<PointOfInterest>> FindPublishedByIdsAsync(int[] ids, CancellationToken cancellationToken);
    Task<PoiTranslation?> FindReadyTranslationAsync(int poiId, string language, CancellationToken cancellationToken);
    Task<PoiAudio?> FindReadyAudioAsync(int poiId, string language, CancellationToken cancellationToken);
}
