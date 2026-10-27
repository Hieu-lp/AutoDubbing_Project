using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using AutoDubbing.Data.Contexts;
using AutoDubbing.Data.Entities;
using AutoDubbing.Service.Pois;
using AutoDubbing.Service.Routes;
using Microsoft.EntityFrameworkCore;

namespace AutoDubbing.Service.Monitoring;

public sealed class VisitorPresence(TimeProvider clock)
{
    public const int WindowSeconds = 60;
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> guests = new();
    private void Prune()
    { foreach (var item in guests) if (item.Value <= clock.GetUtcNow().AddSeconds(-WindowSeconds)) guests.TryRemove(item.Key, out _); }
    public void Touch(Guid id) { Prune(); if (id != Guid.Empty && (guests.ContainsKey(id) || guests.Count < 10000)) guests[id] = clock.GetUtcNow(); }
    public int Count { get { Prune(); return guests.Count; } }
}

public sealed class UsageInput : IValidatableObject
{
    public Guid EventId { get; init; }
    public Guid GuestId { get; init; }
    [Required, RegularExpression("^(auto_play|manual_play|route_selection)$")] public string EventType { get; init; } = "";
    [Range(1, int.MaxValue)] public int? PoiId { get; init; }
    [Range(1, int.MaxValue)] public int? RouteId { get; init; }
    [Range(1, int.MaxValue)] public int? AudioId { get; init; }
    [StringLength(35)] public string? Language { get; init; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (EventId == Guid.Empty || GuestId == Guid.Empty) yield return new("Thiếu mã sự kiện hoặc mã khách hợp lệ.");
        if (EventType == "route_selection")
        { if (RouteId is null || PoiId is not null || AudioId is not null) yield return new("Sự kiện lộ trình cần đúng routeId."); }
        else if (PoiId is null || AudioId is null || RouteId is not null || !LanguageTags.TryNormalize(Language, out _))
            yield return new("Lượt nghe cần POI, audio và ngôn ngữ thực tế hợp lệ.");
    }
}

public sealed class ActivityService(AppDbContext db, IPoiReadService content, SavedRouteService routes,
    VisitorPresence presence, TimeProvider clock)
{
    public async Task<bool> RecordAsync(UsageInput input, CancellationToken token)
    {
        if (await db.UsageEvents.AnyAsync(item => item.EventId == input.EventId, token)) return false;
        if (input.EventType == "route_selection")
        {
            if (!(await routes.ListAsync(true, token)).Any(route => route.Id == input.RouteId))
                throw new ArgumentException("Lộ trình không còn công khai.");
        }
        else
        {
            var saved = await content.ContentAsync(input.PoiId!.Value, input.Language!, token);
            if (saved is null || saved.IsFallback || saved.Audio?.Id != input.AudioId)
                throw new ArgumentException("Audio không còn phù hợp với POI hoặc ngôn ngữ thực tế.");
        }
        var item = new UsageEvent { EventId = input.EventId, GuestId = input.GuestId,
            PointOfInterestId = input.PoiId, RouteId = input.RouteId, Language = input.Language?.ToLowerInvariant() ?? "",
            EventType = input.EventType, OccurredAt = clock.GetUtcNow().UtcDateTime };
        db.UsageEvents.Add(item);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException)
        {
            db.Entry(item).State = EntityState.Detached;
            if (await db.UsageEvents.AnyAsync(row => row.EventId == input.EventId, token)) return false;
            throw;
        }
        presence.Touch(input.GuestId); return true;
    }
}
