using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace AutoDubbing.Data.Storage;

public sealed class SupabaseOptions
{
    public string? ProjectUrl { get; set; }
    public string? Bucket { get; set; }
    public string? UploadKey { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public bool IsConfigured => Uri.TryCreate(ProjectUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort && uri.AbsolutePath == "/" && uri.Query == "" && uri.Fragment == "" && uri.UserInfo == ""
        && Bucket is not null && Regex.IsMatch(Bucket, @"\A[a-z0-9][a-z0-9_-]{0,62}\z")
        && !string.IsNullOrWhiteSpace(UploadKey) && UploadKey.All(c => c is > ' ' and <= '~')
        && (UploadKey.StartsWith("sb_secret_", StringComparison.Ordinal) || IsServiceRole(UploadKey))
        && TimeoutSeconds is >= 1 and <= 120;
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
        // Supabase's direct Storage hostname avoids the project API gateway for file bodies.
        // Derive only from a single hosted project label; never accept a different project/host.
        var projectHost = new Uri(settings.ProjectUrl).Host;
        var projectRef = projectHost[..^".supabase.co".Length];
        var uploadStorage = Regex.IsMatch(projectRef, @"\A[a-z0-9]+\z")
            ? $"https://{projectRef}.storage.supabase.co/storage/v1" : storage;
        var publicUrl = $"{storage}/object/public/{settings.Bucket}/{objectPath}";
        var timer = Stopwatch.StartNew();
        var stage = "CheckingBucket";
        long written = 0;
        int? httpStatus = null;
        string? storageCode = null;
        void BeginStage(string value) { stage = value; httpStatus = null; storageCode = null; }
        AudioStorageException Failure(string code, Exception? exception = null)
        {
            var chain = new List<StorageExceptionInfo>();
            for (var error = exception; error is not null && chain.Count < 8; error = error.InnerException)
                chain.Add(new(error.GetType().FullName!, error.HResult,
                    (error as HttpRequestException)?.HttpRequestError.ToString(),
                    (error as SocketException)?.SocketErrorCode.ToString(), (error as SocketException)?.NativeErrorCode));
            return new(code, new(stage, timer.Elapsed.TotalSeconds, written, wav.Length, httpStatus, storageCode, chain));
        }
        AudioStorageException ResponseFailure() => Failure(storageCode switch
        {
            "NoSuchBucket" => "SupabaseBucketNotFound",
            "EntityTooLarge" or "PayloadTooLarge" => "SupabaseFileLimitExceeded",
            "InvalidMimeType" => "SupabaseMimeRejected",
            "InvalidJWT" or "InvalidApiKey" or "AccessDenied" or "Unauthorized" => "SupabaseUnauthorized",
            _ => httpStatus switch
            {
                401 or 403 => "SupabaseUnauthorized",
                413 => "SupabaseFileLimitExceeded",
                429 => "SupabaseQuotaExceeded",
                >= 500 => "SupabaseServiceUnavailable",
                _ => "SupabaseUploadFailed"
            }
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        async Task<bool> VerifyPublicAsync(bool allowMissing)
        {
            // Public availability is part of readiness: no apikey or bearer on this GET.
            using var request = new HttpRequestMessage(HttpMethod.Get, publicUrl);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            httpStatus = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                storageCode = await ReadStorageCodeAsync(response, timeout.Token);
                if (allowMissing && (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
                    && storageCode == "NoSuchKey") return false;
                throw ResponseFailure();
            }
            await response.Content.LoadIntoBufferAsync(6 * 1024 * 1024, timeout.Token);
            var existing = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            var mime = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (mime is not ("audio/wav" or "audio/x-wav" or "audio/wave") || !existing.AsSpan().SequenceEqual(wav))
                throw Failure("SupabaseObjectMismatch");
            return true;
        }
        try
        {
            using (var check = Request(HttpMethod.Get, $"{storage}/bucket/{settings.Bucket}"))
            using (var checkedBucket = await client.SendAsync(check, HttpCompletionOption.ResponseHeadersRead, timeout.Token))
            {
                await checkedBucket.Content.LoadIntoBufferAsync(64 * 1024, timeout.Token);
                httpStatus = (int)checkedBucket.StatusCode;
                if (!checkedBucket.IsSuccessStatusCode)
                {
                    storageCode = await ReadStorageCodeAsync(checkedBucket, timeout.Token);
                    throw ResponseFailure();
                }
                using var json = JsonDocument.Parse(await checkedBucket.Content.ReadAsStringAsync(timeout.Token));
                if (!json.RootElement.TryGetProperty("public", out var isPublic) || isPublic.ValueKind != JsonValueKind.True)
                    throw Failure("SupabaseBucketNotPublic");
            }
            BeginStage("CheckingExistingObject");
            if (await VerifyPublicAsync(allowMissing: true)) return publicUrl;
            BeginStage("Uploading");
            using var request = Request(HttpMethod.Post, $"{uploadStorage}/object/{settings.Bucket}/{objectPath}");
            request.Headers.Add("x-upsert", "false");
            request.Content = new UploadContent(wav, count => written = count,
                () => stage = "WaitingForUploadResponse");
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            httpStatus = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                storageCode = await ReadStorageCodeAsync(response, timeout.Token);
                // Another request may have uploaded the same deterministic path meanwhile.
                if (response.StatusCode != HttpStatusCode.Conflict) throw ResponseFailure();
            }
            BeginStage("VerifyingPublicObject");
            await VerifyPublicAsync(allowMissing: false);
            return publicUrl;
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested) { throw Failure("SupabaseTimeout", error); }
        catch (HttpRequestException error) { throw Failure("SupabaseUnavailable", error); }
        catch (IOException error) { throw Failure("SupabaseUnavailable", error); }
        catch (JsonException error) { throw Failure("SupabaseInvalidResponse", error); }
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
    private static async Task<string?> ReadStorageCodeAsync(HttpResponseMessage response, CancellationToken token)
    {
        await response.Content.LoadIntoBufferAsync(64 * 1024, token);
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            foreach (var field in new[] { "code", "error" })
                if (json.RootElement.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString() switch
                    {
                        "NoSuchKey" or "NoSuchBucket" or "EntityTooLarge" or "PayloadTooLarge"
                            or "InvalidMimeType" or "InvalidJWT" or "InvalidApiKey" or "AccessDenied"
                            or "Unauthorized" or "ResourceAlreadyExists" or "KeyAlreadyExists"
                            or "InternalError" or "DatabaseError" => value.GetString(),
                        _ => "UnrecognizedStorageCode"
                    };
        }
        catch (JsonException) { }
        return null;
    }
    private sealed class UploadContent(byte[] data, Action<long> progress, Action complete) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = data.Length; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken token)
        {
            long written = 0;
            for (var offset = 0; offset < data.Length; offset += 16384)
            {
                var count = Math.Min(16384, data.Length - offset);
                await stream.WriteAsync(data.AsMemory(offset, count), token);
                written += count;
                progress(written);
            }
            complete();
        }
    }
}
