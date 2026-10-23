using AutoDubbing.Data.Repositories;
using AutoDubbing.Data.Storage;
using AutoDubbing.Service.Languages;
using Microsoft.EntityFrameworkCore;

namespace AutoDubbing.Service.ContentGeneration;

public sealed class ContentGenerationService(IPoiReadRepository read, IContentGenerationRepository write,
    IContentProvider provider, IAudioStorage storage, IAudioCache cache, GenerationCoordinator coordinator)
    : IContentGenerationService
{
    public bool IsConfigured => provider.IsConfigured && storage.IsConfigured;
    public async Task<GenerationBatchResult?> GenerateAsync(int poiId, IReadOnlyList<string> languages,
        int batchSize, CancellationToken token)
    {
        if (batchSize is < 1 or > 3 || (languages.Count < 1 || languages.Count > LanguageCatalog.All.Count)
            || languages.Any(code => LanguageCatalog.Find(code) is null)
            || languages.Distinct(StringComparer.OrdinalIgnoreCase).Count() != languages.Count)
            throw new ArgumentException("Danh sách ngôn ngữ hoặc batchSize không hợp lệ.");
        if (!IsConfigured) throw new GenerationException("GenerationNotConfigured");
        var gate = coordinator.PoiLock(poiId);
        await gate.WaitAsync(token);
        try
        {
            var poi = await read.FindPublishedAsync(poiId, token);
            if (poi is null) return null;
            if (poi.OriginalLanguage != "vi" || string.IsNullOrWhiteSpace(poi.OriginalDescription))
                throw new GenerationException("UnsupportedSourceContent");
            if (poi.OriginalDescription.Length > 6000) throw new GenerationException("SourceTooLong");
            var source = poi.OriginalDescription;
            var results = new List<LanguageGenerationResult>();
            var attempted = 0;
            var paused = false;
            foreach (var code in languages)
            {
                token.ThrowIfCancellationRequested();
                var language = LanguageCatalog.Find(code)!;
                var translation = language.Code == "vi" ? null : await read.FindReadyTranslationAsync(poiId, language.Code, token);
                var text = language.Code == "vi" ? source : translation?.Text;
                var textReady = !string.IsNullOrWhiteSpace(text);
                var audio = textReady ? await read.FindReadyAudioAsync(poiId, language.Code, token) : null;
                if (audio is not null && GeneratedAudioPaths.IsHttps(audio.FilePath)
                    && (!GeneratedAudioPaths.IsManaged(audio.FilePath)
                        || GeneratedAudioPaths.Matches(audio.FilePath, poiId, language.Code, text!)))
                {
                    results.Add(new(language.Code, "SkippedReady", true, true));
                    continue;
                }
                if (paused || attempted >= batchSize)
                {
                    results.Add(new(language.Code, "Deferred", textReady, false));
                    continue;
                }
                attempted++;
                await coordinator.Slots.WaitAsync(token);
                try
                {
                    if (!textReady)
                    {
                        text = await provider.TranslateAsync(source, poi.Name, language, token);
                        if (string.IsNullOrWhiteSpace(text)) throw new GenerationException("InvalidTranslation");
                        if (!await write.SaveTranslationAsync(poiId, language.Code, text, source, token))
                            throw new GenerationException("SourceChanged");
                        textReady = true;
                    }
                    var path = GeneratedAudioPaths.For(poiId, language.Code, text!);
                    var cached = await cache.ReadAsync(path, token);
                    var generated = cached is null ? await provider.SpeakAsync(text!, language, token)
                        : WavAudio.FromProvider(cached, "audio/wav");
                    // Validate container/duration ourselves before uploading or saving SQL metadata.
                    generated = WavAudio.FromProvider(generated.Wav, "audio/wav");
                    if (generated.Wav.Length > 6 * 1024 * 1024) throw new GenerationException("AudioTooLarge");
                    if (cached is null) await cache.WriteAsync(path, generated.Wav, token);
                    var url = await storage.UploadAsync(path, generated.Wav, token);
                    if (!GeneratedAudioPaths.Matches(url, poiId, language.Code, text!))
                        throw new GenerationException("InvalidUploadedAudioUrl");
                    if (!await write.SaveAudioAsync(poiId, language.Code, text!, source, url,
                            checked((int)Math.Ceiling(generated.DurationSeconds)), token))
                        throw new GenerationException("SourceChanged");
                    results.Add(new(language.Code, "Completed", true, true, AudioDurationSeconds: generated.DurationSeconds));
                    try { cache.Remove(path); } catch (IOException) { /* SQL Ready đã lưu; cache dư không làm mất kết quả. */ }
                }
                catch (GenerationException error)
                {
                    results.Add(new(language.Code, error.Code == "QuotaExceeded" ? "PausedQuota" : "Failed", textReady, false,
                        error.Code, FailureDetails: error.Details));
                    paused = error.Code is "QuotaExceeded" or "GeminiUnauthorized" or "GeminiForbidden" or "GeminiModelUnavailable" or "SourceChanged";
                }
                catch (AudioStorageException error)
                {
                    results.Add(new(language.Code, error.Code == "SupabaseQuotaExceeded" ? "PausedQuota" : "Failed", textReady, false,
                        error.Code, StorageFailureDetails: error.Details));
                    paused = true; // Không sinh thêm audio khi storage đang lỗi.
                }
                catch (Exception error) when (error is DbUpdateException or System.Data.Common.DbException or IOException or UnauthorizedAccessException)
                {
                    results.Add(new(language.Code, "Failed", textReady, false, "PersistenceUnavailable"));
                    paused = true;
                }
                finally { coordinator.Slots.Release(); }
            }
            return new(poiId, results, results.Where(result => !result.AudioReady).Select(result => result.Language).ToArray());
        }
        finally { gate.Release(); }
    }
}
