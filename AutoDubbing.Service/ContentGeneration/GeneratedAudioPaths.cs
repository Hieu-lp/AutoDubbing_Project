using System.Security.Cryptography;
using System.Text;

namespace AutoDubbing.Service.ContentGeneration;

public static class GeneratedAudioPaths
{
    public static string For(int poiId, string language, string text) =>
        $"generated/poi-{poiId}/{language}/{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))}.wav";
    public static bool IsHttps(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
    public static bool Matches(string url, int poiId, string language, string text) =>
        IsHttps(url) && new Uri(url).AbsolutePath.EndsWith("/" + For(poiId, language, text), StringComparison.Ordinal);
    public static bool IsManaged(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.AbsolutePath.Contains("/generated/poi-", StringComparison.Ordinal);
}
