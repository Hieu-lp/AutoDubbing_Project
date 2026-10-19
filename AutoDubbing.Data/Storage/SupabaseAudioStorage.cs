using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace AutoDubbing.Data.Storage;

public sealed class SupabaseOptions
{
    public string? ProjectUrl { get; set; }
    public string? Bucket { get; set; }
    public string? UploadKey { get; set; }
    public bool IsConfigured => Uri.TryCreate(ProjectUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort && uri.AbsolutePath == "/" && uri.Query == "" && uri.Fragment == "" && uri.UserInfo == ""
        && Bucket is not null && Regex.IsMatch(Bucket, @"\A[a-z0-9][a-z0-9_-]{0,62}\z")
        && !string.IsNullOrWhiteSpace(UploadKey) && UploadKey.All(c => c is > ' ' and <= '~')
        && (UploadKey.StartsWith("sb_secret_", StringComparison.Ordinal) || IsServiceRole(UploadKey));
    private static bool IsServiceRole(string key)
    {
        try
        {
            var segments = key.Split('.');
            if (segments.Length != 3) return false;
            var payload = segments[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.TryGetProperty("role", out var role) && role.GetString() == "service_role";
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException) { return false; }
    }
}

public sealed class SupabaseAudioStorage(HttpClient client, IOptions<SupabaseOptions> options) : IAudioStorage
{
    public bool IsConfigured => options.Value.IsConfigured;

    public async Task<string> UploadAsync(string objectPath, byte[] wav, CancellationToken token)
    {
        if (!IsConfigured) throw new AudioStorageException("SupabaseNotConfigured");
        if (!Regex.IsMatch(objectPath, @"\Agenerated/poi-[1-9]\d*/[a-z-]+/[a-f0-9]{64}\.wav\z")
            || wav.Length is < 44 or > 6 * 1024 * 1024) throw new AudioStorageException("InvalidAudioUpload");
        var settings = options.Value;
        var storage = settings.ProjectUrl!.TrimEnd('/') + "/storage/v1";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using (var check = Request(HttpMethod.Get, $"{storage}/bucket/{settings.Bucket}"))
            using (var checkedBucket = await client.SendAsync(check, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
            {
                await checkedBucket.Content.LoadIntoBufferAsync(64 * 1024, timeout.Token);
                if (!checkedBucket.IsSuccessStatusCode) throw Failure(checkedBucket.StatusCode);
                using var json = JsonDocument.Parse(await checkedBucket.Content.ReadAsStringAsync(timeout.Token));
                if (!json.RootElement.TryGetProperty("public", out var isPublic) || isPublic.ValueKind != JsonValueKind.True)
                    throw new AudioStorageException("SupabaseBucketNotPublic");
            }
            using var request = Request(HttpMethod.Post, $"{storage}/object/{settings.Bucket}/{objectPath}");
            request.Headers.Add("x-upsert", "true");
            request.Content = new ByteArrayContent(wav);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw Failure(response.StatusCode);
            return $"{storage}/object/public/{settings.Bucket}/{objectPath}";
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new AudioStorageException("SupabaseTimeout"); }
        catch (HttpRequestException) { throw new AudioStorageException("SupabaseUnavailable"); }
        catch (IOException) { throw new AudioStorageException("SupabaseUnavailable"); }
        catch (JsonException) { throw new AudioStorageException("SupabaseInvalidResponse"); }
    }
    private HttpRequestMessage Request(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("apikey", options.Value.UploadKey);
        // New sb_secret keys are not JWTs. Legacy service_role additionally uses Bearer.
        if (!options.Value.UploadKey!.StartsWith("sb_secret_", StringComparison.Ordinal))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.UploadKey);
        return request;
    }
    private static AudioStorageException Failure(HttpStatusCode code) => new(code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "SupabaseUnauthorized",
        HttpStatusCode.TooManyRequests => "SupabaseQuotaExceeded",
        HttpStatusCode.NotFound => "SupabaseBucketNotFound",
        _ => "SupabaseUploadFailed"
    });
}
