using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoDubbing.Service.ContentGeneration;

public static class GeneratedAudioPaths
{
    public static string For(int poiId, string language, string text) =>
        $"generated/poi-{poiId}/{language}/{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))}.wav";
    // TTS is not byte-deterministic across machines. Keep the text key for cache lookup,
    // but give each immutable uploaded rendition its own audio hash.
    public static string For(int poiId, string language, string text, byte[] wav) =>
        For(poiId, language, text)[..^4] + "-" + Convert.ToHexStringLower(SHA256.HashData(wav)) + ".wav";
    public static bool IsHttps(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo);
    public static bool Matches(string url, int poiId, string language, string text) =>
        IsHttps(url) && Regex.IsMatch(new Uri(url).AbsolutePath,
            "/" + Regex.Escape(For(poiId, language, text)[..^4]) + @"(?:-[a-f0-9]{64})?\.wav\z");
    public static bool IsManaged(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.AbsolutePath.Contains("/generated/poi-", StringComparison.Ordinal);
}
