using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using AutoDubbing.Data.Contexts;
using AutoDubbing.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoDubbing.Service.Routes;

public sealed record SavedRouteInput : IValidatableObject
{
    [Required, StringLength(200, MinimumLength = 1)] public string Name { get; init; } = "";
    [Required, MinLength(1), MaxLength(OrsDirectionsOptions.MaxPoiCount)] public int[] PoiIds { get; init; } = [];
    [Required, RegularExpression("^(Published|Hidden)$")] public string Status { get; init; } = "Hidden";
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(Name)) yield return new("Tên không được rỗng.", [nameof(Name)]);
        if (PoiIds is not null && (PoiIds.Any(id => id <= 0) || PoiIds.Distinct().Count() != PoiIds.Length))
            yield return new("POI phải có ID dương và không trùng; thứ tự được giữ nguyên.", [nameof(PoiIds)]);
    }
}
public sealed record SavedRoutePoi(int Id, string Name, double Latitude, double Longitude, bool IsPublic);
public sealed record SavedRouteDto(int Id, string Name, int[] PoiIds, string Status, DateTime UpdatedAtUtc,
    IReadOnlyList<SavedRoutePoi> Pois, int[] UnavailablePoiIds, string Version);

public sealed class SavedRouteService(AppDbContext db)
{
    public static int[]? ParseIds(string json)
    {
        try
        {
            var ids = JsonSerializer.Deserialize<int[]>(json);
            return ids is { Length: > 0 and <= OrsDirectionsOptions.MaxPoiCount }
                && ids.All(id => id > 0) && ids.Distinct().Count() == ids.Length ? ids : null;
        }
        catch (JsonException) { return null; }
    }
    public async Task<IReadOnlyList<SavedRouteDto>> ListAsync(bool publishedOnly, CancellationToken token)
    {
        var rows = await db.Routes.AsNoTracking().Where(route => route.Status != "Deleted"
            && (!publishedOnly || route.Status == "Published")).OrderBy(route => route.Id).ToListAsync(token);
        var points = await db.PointsOfInterest.AsNoTracking().ToDictionaryAsync(poi => poi.Id, token);
        var result = new List<SavedRouteDto>();
        foreach (var row in rows)
        {
            var ids = ParseIds(row.PoiIdsJson);
            if (ids is null) { if (publishedOnly) continue; ids = []; }
            var unavailable = ids.Where(id => !points.TryGetValue(id, out var poi) || poi.Status != "Published").ToArray();
            if (publishedOnly && unavailable.Length > 0) continue;
            var pois = ids.Where(points.ContainsKey).Select(id => points[id]).Select(poi =>
                new SavedRoutePoi(poi.Id, poi.Name, poi.Latitude, poi.Longitude, poi.Status == "Published")).ToArray();
            var version = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                new { row.Name, row.Status, ids, pois }))));
            result.Add(new(row.Id, row.Name, ids, row.Status, row.UpdatedAt, pois, unavailable, version));
        }
        return result;
    }
    public async Task<SavedRouteDto?> SaveAsync(int? id, SavedRouteInput input, CancellationToken token)
    {
        var pois = await db.PointsOfInterest.AsNoTracking().Where(poi => input.PoiIds.Contains(poi.Id)).ToListAsync(token);
        if (pois.Count != input.PoiIds.Length || input.Status == "Published" && pois.Any(poi => poi.Status != "Published"))
            throw new ArgumentException("POI không tồn tại; lộ trình xuất bản chỉ được chứa POI đã xuất bản.");
        var row = id is null ? new TourRoute() : await db.Routes.SingleOrDefaultAsync(route => route.Id == id && route.Status != "Deleted", token);
        if (row is null) return null;
        row.Name = input.Name.Trim(); row.PoiIdsJson = JsonSerializer.Serialize(input.PoiIds);
        row.Status = input.Status; row.UpdatedAt = DateTime.UtcNow;
        if (id is null) db.Routes.Add(row);
        await db.SaveChangesAsync(token);
        return (await ListAsync(false, token)).Single(route => route.Id == row.Id);
    }
    public async Task<bool> DeleteAsync(int id, CancellationToken token)
    {
        var row = await db.Routes.SingleOrDefaultAsync(route => route.Id == id && route.Status != "Deleted", token);
        if (row is null) return false;
        // Keep route IDs/names referenced by historical usage; public/admin lists exclude deleted rows.
        row.Status = "Deleted"; row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(token); return true;
    }
}
