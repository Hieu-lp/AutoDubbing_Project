using AutoDubbing.Data.Contexts;
using AutoDubbing.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoDubbing.Data.Repositories;

public interface IContentGenerationRepository
{
    Task<bool> SaveTranslationAsync(int poiId, string language, string text, string expectedSource, CancellationToken token, string? expectedVersion = null);
    Task<bool> SaveAudioAsync(int poiId, string language, string text, string expectedSource,
        string url, int seconds, CancellationToken token, string? expectedVersion = null);
}

public sealed class ContentGenerationRepository(AppDbContext db) : IContentGenerationRepository
{
    // Transaction chỉ bao quanh kiểm tra/lưu SQL, không bao quanh dịch/TTS/upload.
    public async Task<bool> SaveTranslationAsync(int poiId, string language, string text, string expectedSource, CancellationToken token, string? expectedVersion = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        if (!await SourceMatchesAsync(poiId, expectedSource, token, expectedVersion)) return false;
        var sourceVersion = (await db.PointsOfInterest.AsNoTracking().SingleAsync(poi => poi.Id == poiId, token)).SourceVersion;
        db.Translations.Add(new PoiTranslation { PointOfInterestId = poiId, Language = language,
            Text = text, Status = PoiContentStatus.Ready, UpdatedAt = DateTime.UtcNow, SourceVersion = sourceVersion });
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task<bool> SaveAudioAsync(int poiId, string language, string text, string expectedSource,
        string url, int seconds, CancellationToken token, string? expectedVersion = null)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        if (!await SourceMatchesAsync(poiId, expectedSource, token, expectedVersion)) return false;
        var sourceVersion = (await db.PointsOfInterest.AsNoTracking().SingleAsync(poi => poi.Id == poiId, token)).SourceVersion;
        if (language != "vi")
        {
            var current = await new PoiReadRepository(db).FindReadyTranslationAsync(poiId, language, token);
            if (current?.Text != text) return false;
        }
        else if (text != expectedSource) return false;
        db.Audios.Add(new PoiAudio { PointOfInterestId = poiId, Language = language,
            FilePath = url, Duration = seconds, Status = PoiContentStatus.Ready, SourceVersion = sourceVersion,
            TextHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))) });
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return true;
    }

    private async Task<bool> SourceMatchesAsync(int poiId, string source, CancellationToken token, string? expectedVersion)
    {
        var poi = await db.PointsOfInterest.AsNoTracking().SingleOrDefaultAsync(poi => poi.Id == poiId, token);
        // So sánh ordinal phía .NET để cả khoảng trắng cuối cũng phải khớp.
        return poi is not null && poi.Status == PoiPublicationStatus.Published
            && poi.OriginalLanguage == "vi" && poi.OriginalDescription == source
            && (expectedVersion is null || poi.SourceVersion == expectedVersion);
    }
}
