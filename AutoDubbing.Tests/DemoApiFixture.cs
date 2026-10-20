using AutoDubbing.Data.Contexts;
using AutoDubbing.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AutoDubbing.Tests;

internal sealed class DemoApiFixture : IAsyncDisposable
{
    public AdminApiFactory Factory { get; }
    public HttpClient Client { get; }
    private int adminId;

    private DemoApiFixture(string? routingApiKey)
    {
        Factory = new AdminApiFactory { RoutingApiKey = routingApiKey };
        Client = Factory.CreateHttpsClient();
    }

    public static async Task<DemoApiFixture> CreateAsync(string? routingApiKey = "test-only-ors-key")
    {
        var fixture = new DemoApiFixture(routingApiKey);
        try
        {
            fixture.adminId = await fixture.Factory.SeedAdminAsync();
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    public async Task<int> AddPoiAsync(string name = "POI fixture", double latitude = 0, double longitude = 0,
        string status = PoiPublicationStatus.Published, double proximityRadius = 50, string language = "vi")
    {
        var poi = new PointOfInterest
        {
            UserId = adminId, Name = name, Latitude = latitude, Longitude = longitude,
            ProximityRadius = proximityRadius, Status = status, OriginalLanguage = language,
            OriginalDescription = "Nội dung gốc", ThumbnailUrl = null
        };
        await WriteAsync(db => db.PointsOfInterest.Add(poi));
        return poi.Id;
    }

    public async Task<int> AddTranslationAsync(int poiId, string language, string text,
        string status = PoiContentStatus.Ready, DateTime? updatedAt = null)
    {
        var translation = new PoiTranslation
        {
            PointOfInterestId = poiId, Language = language, Text = text, Status = status,
            UpdatedAt = updatedAt ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        await WriteAsync(db => db.Translations.Add(translation));
        return translation.Id;
    }

    public async Task<int> AddAudioAsync(int poiId, string language, string url = "https://example.test/audio.mp3",
        string status = PoiContentStatus.Ready)
    {
        var audio = new PoiAudio { PointOfInterestId = poiId, Language = language, FilePath = url, Status = status };
        await WriteAsync(db => db.Audios.Add(audio));
        return audio.Id;
    }

    private async Task WriteAsync(Action<AppDbContext> write)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
        {
            throw new InvalidOperationException("Fixture chỉ được ghi vào SQLite in-memory.");
        }
        write(db);
        await db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
    }
}
