using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AutoDubbing.Data.Storage;
using AutoDubbing.Service.ContentGeneration;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoDubbing.Tests;

public class SupabaseStorageTests
{
    private sealed class Stub(bool publicBucket, HttpStatusCode uploadStatus = HttpStatusCode.OK,
        string projectHost = "fixture.supabase.co") : HttpMessageHandler
    {
        public string ProjectHost { get; } = projectHost;
        public int Uploads;
        public Dictionary<string, byte[]> Objects { get; } = new();
        public bool LoseUploadResponse { get; set; }
        public bool HangUpload { get; set; }
        public bool PublicReadFailsAfterUpload { get; set; }
        public string MissingCode { get; set; } = "NoSuchKey";
        public string? UploadErrorCode { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(request.Method == HttpMethod.Post ? "fixture.storage.supabase.co" : ProjectHost, request.RequestUri!.Host);
            var path = request.RequestUri.AbsolutePath;
            if (path.StartsWith("/storage/v1/object/public/audio/", StringComparison.Ordinal))
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.False(request.Headers.Contains("apikey"));
                Assert.Null(request.Headers.Authorization);
                var name = path["/storage/v1/object/public/audio/".Length..];
                if (Objects.TryGetValue(name, out var bytes) && !PublicReadFailsAfterUpload)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                    response.Content.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                    return response;
                }
                return new(HttpStatusCode.BadRequest) { Content = new StringContent(JsonSerializer.Serialize(new { code = MissingCode })) };
            }
            Assert.Equal("sb_secret_fixture", Assert.Single(request.Headers.GetValues("apikey")));
            Assert.Null(request.Headers.Authorization); // secret keys are not JWTs
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { @public = publicBucket })) };
            Uploads++;
            Assert.Equal("audio/wav", request.Content!.Headers.ContentType!.MediaType);
            Assert.Equal("false", Assert.Single(request.Headers.GetValues("x-upsert")));
            var data = await request.Content.ReadAsByteArrayAsync(token);
            Assert.Equal(data.Length, request.Content.Headers.ContentLength);
            if (HangUpload) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            if (uploadStatus is HttpStatusCode.OK or HttpStatusCode.Conflict)
                Objects[path["/storage/v1/object/audio/".Length..]] = data;
            if (LoseUploadResponse) throw new HttpRequestException("sb_secret_fixture", new IOException("sensitive-error-must-not-leak"));
            return new(uploadStatus) { Content = new StringContent(JsonSerializer.Serialize(new { code = UploadErrorCode, message = "sensitive-error-must-not-leak" })) };
        }
    }
    private static SupabaseAudioStorage Storage(Stub stub, int seconds = 30) => new(new HttpClient(stub), Options.Create(new SupabaseOptions
    { ProjectUrl = "https://" + stub.ProjectHost, Bucket = "audio", UploadKey = "sb_secret_fixture", TimeoutSeconds = seconds }));

    [Fact]
    public async Task Public_bucket_upload_returns_https_url_without_credentials()
    {
        using var stub = new Stub(true);
        var path = GeneratedAudioPaths.For(1, "vi", "Nội dung");
        var url = await Storage(stub).UploadAsync(path, new byte[44], default);
        Assert.Equal("https://fixture.supabase.co/storage/v1/object/public/audio/" + path, url);
        Assert.DoesNotContain("sb_secret_fixture", url);
        Assert.Equal(1, stub.Uploads);
    }
    [Fact]
    public async Task Already_direct_storage_host_is_not_rewritten_to_a_different_hostname()
    {
        using var stub = new Stub(true, projectHost: "fixture.storage.supabase.co");
        var path = GeneratedAudioPaths.For(1, "vi", "Nội dung");
        var url = await Storage(stub).UploadAsync(path, new byte[44], default);
        Assert.Equal("https://fixture.storage.supabase.co/storage/v1/object/public/audio/" + path, url);
        Assert.Equal(1, stub.Uploads);
    }
    [Fact]
    public async Task Private_bucket_is_rejected_before_upload()
    {
        using var stub = new Stub(false);
        var error = await Assert.ThrowsAsync<AudioStorageException>(() =>
            Storage(stub).UploadAsync(GeneratedAudioPaths.For(1, "vi", "Nội dung"), new byte[44], default));
        Assert.Equal("SupabaseBucketNotPublic", error.Code);
        Assert.Equal(0, stub.Uploads);
    }
    [Theory]
    [InlineData(401, "SupabaseUnauthorized")]
    [InlineData(403, "SupabaseUnauthorized")]
    [InlineData(429, "SupabaseQuotaExceeded")]
    public async Task Errors_are_sanitized_and_not_assumed_uploaded(int status, string code)
    {
        using var stub = new Stub(true, (HttpStatusCode)status);
        var error = await Assert.ThrowsAsync<AudioStorageException>(() =>
            Storage(stub).UploadAsync(GeneratedAudioPaths.For(1, "vi", "Nội dung"), new byte[44], default));
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain("sensitive-error", error.ToString());
        Assert.DoesNotContain("sensitive-error", JsonSerializer.Serialize(error.Details));
        Assert.Equal(status, error.Details?.HttpStatus);
        Assert.Equal(1, stub.Uploads);
    }
    [Theory]
    [InlineData("http://fixture.supabase.co", "audio", "sb_secret_fixture")]
    [InlineData("https://elsewhere.test", "audio", "sb_secret_fixture")]
    [InlineData("https://fixture.supabase.co", "../audio", "sb_secret_fixture")]
    [InlineData("https://fixture.supabase.co", "audio", "sb_publishable_fixture")]
    public void Invalid_config_rejected_without_network(string url, string bucket, string key) =>
        Assert.False(new SupabaseOptions { ProjectUrl = url, Bucket = bucket, UploadKey = key }.IsConfigured);

    [Fact]
    public async Task Existing_identical_object_is_verified_anonymously_without_upload()
    {
        using var stub = new Stub(true);
        var path = GeneratedAudioPaths.For(1, "zh-hans", "Transcript");
        stub.Objects[path] = new byte[44];
        await Storage(stub).UploadAsync(path, new byte[44], default);
        Assert.Equal(0, stub.Uploads);
        Assert.Single(stub.Objects);
    }

    [Fact]
    public async Task Existing_different_object_is_preserved_instead_of_overwritten()
    {
        using var stub = new Stub(true);
        var path = GeneratedAudioPaths.For(1, "vi", "Transcript");
        var existing = new byte[45];
        stub.Objects[path] = existing;
        var error = await Assert.ThrowsAsync<AudioStorageException>(() => Storage(stub).UploadAsync(path, new byte[44], default));
        Assert.Equal("SupabaseObjectMismatch", error.Code);
        Assert.Equal("CheckingExistingObject", error.Details?.Stage);
        Assert.Equal(0, stub.Uploads);
        Assert.Same(existing, stub.Objects[path]);
    }

    [Fact]
    public async Task Lost_upload_response_reuses_existing_object_on_retry()
    {
        using var stub = new Stub(true) { LoseUploadResponse = true };
        var path = GeneratedAudioPaths.For(1, "vi", "Transcript");
        var error = await Assert.ThrowsAsync<AudioStorageException>(() => Storage(stub).UploadAsync(path, new byte[44], default));
        Assert.Equal("SupabaseUnavailable", error.Code);
        Assert.Equal("WaitingForUploadResponse", error.Details?.Stage);
        Assert.Equal(44, error.Details?.BytesWritten);
        Assert.Null(error.Details?.HttpStatus);
        Assert.Equal(2, error.Details?.ExceptionChain?.Count);
        Assert.DoesNotContain("sb_secret_fixture", JsonSerializer.Serialize(error.Details));
        Assert.DoesNotContain("sensitive-error", JsonSerializer.Serialize(error.Details));
        await Storage(stub).UploadAsync(path, new byte[44], default);
        Assert.Equal(1, stub.Uploads);
        Assert.Single(stub.Objects);
    }

    [Fact]
    public async Task Success_response_without_readable_file_is_not_success()
    {
        using var stub = new Stub(true) { PublicReadFailsAfterUpload = true };
        var error = await Assert.ThrowsAsync<AudioStorageException>(() => Storage(stub).UploadAsync(GeneratedAudioPaths.For(1, "vi", "Transcript"), new byte[44], default));
        Assert.Equal("VerifyingPublicObject", error.Details?.Stage);
        Assert.Equal("NoSuchKey", error.Details?.StorageCode);
        Assert.Equal(1, stub.Uploads);
    }

    [Theory]
    [InlineData("EntityTooLarge", "SupabaseFileLimitExceeded")]
    [InlineData("InvalidMimeType", "SupabaseMimeRejected")]
    [InlineData("InvalidJWT", "SupabaseUnauthorized")]
    [InlineData("NoSuchBucket", "SupabaseBucketNotFound")]
    public async Task Storage_error_code_distinguishes_different_400_responses(string providerCode, string code)
    {
        using var stub = new Stub(true, HttpStatusCode.BadRequest) { UploadErrorCode = providerCode };
        var error = await Assert.ThrowsAsync<AudioStorageException>(() => Storage(stub).UploadAsync(GeneratedAudioPaths.For(1, "vi", "Transcript"), new byte[44], default));
        Assert.Equal(code, error.Code);
        Assert.Equal(400, error.Details?.HttpStatus);
        Assert.Equal(providerCode, error.Details?.StorageCode);
    }

    [Fact]
    public async Task Missing_bucket_is_not_misinterpreted_as_a_missing_object()
    {
        using var stub = new Stub(true) { MissingCode = "NoSuchBucket" };
        var error = await Assert.ThrowsAsync<AudioStorageException>(() => Storage(stub).UploadAsync(GeneratedAudioPaths.For(1, "vi", "Transcript"), new byte[44], default));
        Assert.Equal("SupabaseBucketNotFound", error.Code);
        Assert.Equal(0, stub.Uploads);
    }

    [Fact]
    public async Task Race_with_matching_object_is_reconciled_after_conflict()
    {
        using var stub = new Stub(true, HttpStatusCode.Conflict);
        await Storage(stub).UploadAsync(GeneratedAudioPaths.For(1, "vi", "Transcript"), new byte[44], default);
        Assert.Equal(1, stub.Uploads);
        Assert.Single(stub.Objects);
    }

    [Fact]
    public async Task Upload_timeout_reports_write_stage_and_safe_exception_chain()
    {
        using var stub = new Stub(true) { HangUpload = true };
        var error = await Assert.ThrowsAsync<AudioStorageException>(() => Storage(stub, 1).UploadAsync(GeneratedAudioPaths.For(1, "vi", "Transcript"), new byte[44], default));
        Assert.Equal("SupabaseTimeout", error.Code);
        Assert.Equal("WaitingForUploadResponse", error.Details?.Stage);
        Assert.Equal(44, error.Details?.BytesWritten);
        Assert.Null(error.Details?.HttpStatus);
        Assert.NotEmpty(error.Details!.ExceptionChain!);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_instead_of_reported_as_provider_timeout()
    {
        using var stub = new Stub(true) { HangUpload = true };
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Storage(stub).UploadAsync(GeneratedAudioPaths.For(1, "vi", "Transcript"), new byte[44], cancelled.Token));
    }
}
