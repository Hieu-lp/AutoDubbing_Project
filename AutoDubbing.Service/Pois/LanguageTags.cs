using System.Globalization;

namespace AutoDubbing.Service.Pois;

public static class LanguageTags
{
    // Mã culture do .NET hỗ trợ; không tự suy ra ngôn ngữ khác từ ngôn ngữ được yêu cầu.
    private static readonly HashSet<string> KnownTags = CultureInfo
        .GetCultures(CultureTypes.NeutralCultures | CultureTypes.SpecificCultures)
        .Select(culture => culture.Name.ToLowerInvariant())
        .Where(name => name.Length > 0).ToHashSet(StringComparer.Ordinal);

    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = value?.ToLowerInvariant() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value) && value == value.Trim() && KnownTags.Contains(normalized);
    }
}
