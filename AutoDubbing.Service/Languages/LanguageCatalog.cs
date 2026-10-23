namespace AutoDubbing.Service.Languages;

public sealed record SupportedLanguage(string Code, string DisplayName, string TranslationName,
    string? TtsLanguageCode, string Voice = "Kore");

public static class LanguageCatalog
{
    public static IReadOnlyList<SupportedLanguage> All { get; } = Array.AsReadOnly(new[]
    {
        new SupportedLanguage("vi", "Tiếng Việt", "Vietnamese", "vi-VN"),
        new SupportedLanguage("en", "Tiếng Anh (Mỹ)", "English (United States)", "en-US"),
        new SupportedLanguage("zh-hans", "Tiếng Trung giản thể", "Mandarin Chinese (Simplified script)", "cmn-CN"),
        new SupportedLanguage("ja", "Tiếng Nhật", "Japanese", "ja-JP"),
        new SupportedLanguage("ko", "Tiếng Hàn", "Korean", "ko-KR"),
        new SupportedLanguage("fr", "Tiếng Pháp", "French (France)", "fr-FR"),
        new SupportedLanguage("de", "Tiếng Đức", "German", "de-DE"),
        new SupportedLanguage("es", "Tiếng Tây Ban Nha", "Spanish (Spain)", "es-ES"),
        new SupportedLanguage("ru", "Tiếng Nga", "Russian", "ru-RU"),
        new SupportedLanguage("it", "Tiếng Ý", "Italian", "it-IT"),
        new SupportedLanguage("pt", "Tiếng Bồ Đào Nha (Brazil)", "Portuguese (Brazil)", "pt-BR"),
        new SupportedLanguage("th", "Tiếng Thái", "Thai", "th-TH"),
        new SupportedLanguage("id", "Tiếng Indonesia", "Indonesian", "id-ID"),
        // Malay hỗ trợ auto detection; không tự đặt locale chưa được tài liệu hóa.
        new SupportedLanguage("ms", "Tiếng Mã Lai", "Malay", null),
        new SupportedLanguage("hi", "Tiếng Hindi", "Hindi", "hi-IN")
    });
    public static SupportedLanguage? Find(string? code) => code is null || code != code.Trim()
        ? null : All.FirstOrDefault(language => string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase));
}
