using AutoDubbing.Data.Contexts;
using AutoDubbing.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoDubbing.Data.Repositories;

public sealed class PoiReadRepository(AppDbContext dbContext) : IPoiReadRepository
{
    private const string StatusCollation = "Latin1_General_100_BIN2";

    private IQueryable<PointOfInterest> PublishedPois => dbContext.PointsOfInterest.AsNoTracking()
        .Where(poi => EF.Functions.Collate(poi.Status, StatusCollation) == PoiPublicationStatus.Published);

    public Task<List<PointOfInterest>> ListPublishedAsync(CancellationToken cancellationToken) =>
        PublishedPois.OrderBy(poi => poi.Id).ToListAsync(cancellationToken);

    public Task<PointOfInterest?> FindPublishedAsync(int id, CancellationToken cancellationToken) =>
        PublishedPois.SingleOrDefaultAsync(poi => poi.Id == id, cancellationToken);
    public Task<PointOfInterest?> FindForAdminAsync(int id, CancellationToken cancellationToken) =>
        dbContext.PointsOfInterest.AsNoTracking().SingleOrDefaultAsync(poi => poi.Id == id, cancellationToken);

    public Task<List<PointOfInterest>> FindPublishedByIdsAsync(int[] ids, CancellationToken cancellationToken) =>
        PublishedPois.Where(poi => ids.Contains(poi.Id)).ToListAsync(cancellationToken);

    public Task<PoiTranslation?> FindReadyTranslationAsync(int poiId, string language,
        CancellationToken cancellationToken) =>
        dbContext.Translations.AsNoTracking()
            .Where(translation => translation.PointOfInterestId == poiId
                && EF.Functions.Collate(translation.Status, StatusCollation) == PoiContentStatus.Ready
                && translation.Language.ToLower() == language)
            .OrderByDescending(translation => translation.UpdatedAt)
            .ThenByDescending(translation => translation.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PoiAudio?> FindReadyAudioAsync(int poiId, string language, CancellationToken cancellationToken) =>
        dbContext.Audios.AsNoTracking()
            .Where(audio => audio.PointOfInterestId == poiId
                && EF.Functions.Collate(audio.Status, StatusCollation) == PoiContentStatus.Ready
                && audio.Language.ToLower() == language)
            .OrderByDescending(audio => audio.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
