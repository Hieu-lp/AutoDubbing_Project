using AutoDubbing.Data.Contexts;
using AutoDubbing.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoDubbing.Service.Pois;

public sealed class PoiManagementService(AppDbContext dbContext) : IPoiManagementService
{
    public async Task<IReadOnlyList<PoiManagementResponse>> ListAsync(
        CancellationToken cancellationToken)
    {
        var pois = await dbContext.PointsOfInterest.AsNoTracking()
            .OrderBy(poi => poi.Id)
            .ToListAsync(cancellationToken);
        return pois.Select(PoiManagementResponse.FromEntity).ToArray();
    }

    public async Task<PoiManagementResponse?> FindAsync(int id, CancellationToken cancellationToken)
    {
        var poi = await dbContext.PointsOfInterest.AsNoTracking()
            .SingleOrDefaultAsync(poi => poi.Id == id, cancellationToken);
        return poi is null ? null : PoiManagementResponse.FromEntity(poi);
    }

    public async Task<PoiManagementResponse> CreateAsync(int adminId, PoiManagementRequest request,
        CancellationToken cancellationToken)
    {
        var poi = new PointOfInterest { UserId = adminId };
        Apply(poi, request);
        dbContext.PointsOfInterest.Add(poi);
        await dbContext.SaveChangesAsync(cancellationToken);
        return PoiManagementResponse.FromEntity(poi);
    }

    public async Task<PoiManagementResponse?> UpdateAsync(int id, PoiManagementRequest request,
        CancellationToken cancellationToken)
    {
        var poi = await dbContext.PointsOfInterest
            .SingleOrDefaultAsync(poi => poi.Id == id, cancellationToken);
        if (poi is null)
        {
            return null;
        }

        Apply(poi, request);
        await dbContext.SaveChangesAsync(cancellationToken);
        return PoiManagementResponse.FromEntity(poi);
    }

    public async Task<PoiDeleteResult> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var poi = await dbContext.PointsOfInterest
            .SingleOrDefaultAsync(poi => poi.Id == id, cancellationToken);
        if (poi is null)
        {
            return PoiDeleteResult.NotFound;
        }

        var hasDependents =
            await dbContext.Translations.AnyAsync(item => item.PointOfInterestId == id, cancellationToken)
            || await dbContext.Audios.AnyAsync(item => item.PointOfInterestId == id, cancellationToken)
            || await dbContext.UsageEvents.AnyAsync(item => item.PointOfInterestId == id, cancellationToken);
        if (hasDependents)
        {
            return PoiDeleteResult.HasDependents;
        }

        dbContext.PointsOfInterest.Remove(poi);
        await dbContext.SaveChangesAsync(cancellationToken);
        return PoiDeleteResult.Deleted;
    }

    private static void Apply(PointOfInterest poi, PoiManagementRequest request)
    {
        poi.Name = request.Name.Trim();
        poi.Latitude = request.Latitude;
        poi.Longitude = request.Longitude;
        poi.ProximityRadius = request.ProximityRadius;
        poi.ThumbnailUrl = string.IsNullOrWhiteSpace(request.ThumbnailUrl)
            ? null
            : request.ThumbnailUrl.Trim();
        poi.OriginalDescription = request.OriginalDescription.Trim();
        poi.OriginalLanguage = request.OriginalLanguage.Trim().ToLowerInvariant();
        poi.Status = request.Status;
    }
}
