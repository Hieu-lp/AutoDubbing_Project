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
        int batchSize, CancellationToken token, string? expectedSourceVersion = null)
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
            if (expectedSourceVersion is not null && ContentPreparation.SourceVersion.For(poi) != expectedSourceVersion)
                throw new GenerationException("SourceChanged");
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
                if (translation?.SourceVersion is { } version && version != poi.SourceVersion) translation = null;
                var text = language.Code == "vi" ? source : translation?.Text;
                var textReady = !string.IsNullOrWhiteSpace(text);
                var audio = textReady ? await read.FindReadyAudioAsync(poiId, language.Code, token) : null;
                var transcriptHash = textReady ? Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text!))) : null;
                if (audio is not null && GeneratedAudioPaths.IsHttps(audio.FilePath)
                    && (audio.SourceVersion is null || audio.SourceVersion == poi.SourceVersion)
                    && (audio.TextHash is null || audio.TextHash == transcriptHash)
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
                var stage = "translation";
                try
                {
                    if (!textReady)
                    {
                        text = await provider.TranslateAsync(source, poi.Name, language, token);
                        if (string.IsNullOrWhiteSpace(text)) throw new GenerationException("InvalidTranslation");
                        stage = "persistence";
                        if (!await write.SaveTranslationAsync(poiId, language.Code, text, source, token, poi.SourceVersion))
                            throw new GenerationException("SourceChanged");
                        textReady = true;
                    }
                    var path = GeneratedAudioPaths.For(poiId, language.Code, text!);
                    stage = "cache";
                    var cached = await cache.ReadAsync(path, token);
                    stage = cached is null ? "tts" : "cache";
                    var generated = cached is null ? await provider.SpeakAsync(text!, language, token)
                        : WavAudio.FromProvider(cached, "audio/wav");
                    // Validate container/duration ourselves before uploading or saving SQL metadata.
                    generated = WavAudio.FromProvider(generated.Wav, "audio/wav");
                    if (generated.Wav.Length > 6 * 1024 * 1024) throw new GenerationException("AudioTooLarge");
                    if (cached is null)
                    {
                        stage = "cache";
                        await cache.WriteAsync(path, generated.Wav, token);
                    }
                    var uploadPath = GeneratedAudioPaths.For(poiId, language.Code, text!, generated.Wav);
                    stage = "upload";
                    var url = await storage.UploadAsync(uploadPath, generated.Wav, token);
                    if (!GeneratedAudioPaths.Matches(url, poiId, language.Code, text!))
                        throw new GenerationException("InvalidUploadedAudioUrl");
                    stage = "persistence";
                    if (!await write.SaveAudioAsync(poiId, language.Code, text!, source, url,
                            checked((int)Math.Ceiling(generated.DurationSeconds)), token, poi.SourceVersion))
                        throw new GenerationException("SourceChanged");
                    results.Add(new(language.Code, "Completed", true, true, AudioDurationSeconds: generated.DurationSeconds));
                    // Preserve cached WAVs as well as SQL/storage results; no automatic cleanup in this flow.
                }
                catch (GenerationException error)
                {
                    results.Add(new(language.Code, error.Code == "QuotaExceeded" ? "PausedQuota" : "Failed", textReady, false,
                        error.Code, FailureDetails: error.Details, FailureStage: stage));
                    paused = error.Code is "QuotaExceeded" or "GeminiUnauthorized" or "GeminiForbidden" or "GeminiModelUnavailable" or "SourceChanged";
                }
                catch (AudioStorageException error)
                {
                    results.Add(new(language.Code, error.Code == "SupabaseQuotaExceeded" ? "PausedQuota" : "Failed", textReady, false,
                        error.Code, StorageFailureDetails: error.Details, FailureStage: stage));
                    paused = true; // Không sinh thêm audio khi storage đang lỗi.
                }
                catch (Exception error) when (error is DbUpdateException or System.Data.Common.DbException or IOException or UnauthorizedAccessException)
                {
                    results.Add(new(language.Code, "Failed", textReady, false, "PersistenceUnavailable", FailureStage: stage));
                    paused = true;
                }
                finally { coordinator.Slots.Release(); }
            }
            return new(poiId, results, results.Where(result => !result.AudioReady).Select(result => result.Language).ToArray());
        }
        finally { gate.Release(); }
    }
}
