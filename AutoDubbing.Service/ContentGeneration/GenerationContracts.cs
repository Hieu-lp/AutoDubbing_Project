using AutoDubbing.Service.Languages;

namespace AutoDubbing.Service.ContentGeneration;

public sealed class GeminiOptions
{
    public const string TranslationModel = "gemini-3.1-flash-lite";
    public const string AudioModel = "gemini-3.1-flash-tts-preview";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 55;
    public int MaxRetries { get; set; } = 2;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey)
        && ApiKey.All(c => c is > ' ' and <= '~') && TimeoutSeconds is >= 1 and <= 120
        && MaxRetries is >= 0 and <= 3;
}
public sealed class GenerationException(string code) : Exception(code)
{
    public string Code => Message;
}
public sealed record GeneratedAudio(byte[] Wav, double DurationSeconds);
public interface IContentProvider
{
    bool IsConfigured { get; }
    Task<string> TranslateAsync(string source, string poiName, SupportedLanguage target, CancellationToken token);
    Task<GeneratedAudio> SpeakAsync(string text, SupportedLanguage language, CancellationToken token);
}
public interface IGenerationDelay { Task WaitAsync(TimeSpan delay, CancellationToken token); }
public sealed class GenerationDelay : IGenerationDelay
{
    public Task WaitAsync(TimeSpan delay, CancellationToken token) => Task.Delay(delay, token);
}
public sealed record LanguageGenerationResult(string Language, string Status, bool TextReady,
    bool AudioReady, string? ErrorCode = null, double? AudioDurationSeconds = null);
public sealed record GenerationBatchResult(int PoiId, IReadOnlyList<LanguageGenerationResult> Results,
    IReadOnlyList<string> RemainingLanguages);
public interface IContentGenerationService
{
    bool IsConfigured { get; }
    Task<GenerationBatchResult?> GenerateAsync(int poiId, IReadOnlyList<string> languages,
        int batchSize, CancellationToken token);
}
