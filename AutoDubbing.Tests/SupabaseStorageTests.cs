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
    private sealed class Stub(bool publicBucket, HttpStatusCode uploadStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Uploads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("fixture.supabase.co", request.RequestUri!.Host);
            Assert.Equal("sb_secret_fixture", Assert.Single(request.Headers.GetValues("apikey")));
            Assert.Null(request.Headers.Authorization); // secret keys are not JWTs
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { @public = publicBucket })) });
            Uploads++;
            Assert.Equal("audio/wav", request.Content!.Headers.ContentType!.MediaType);
            Assert.Equal("true", Assert.Single(request.Headers.GetValues("x-upsert")));
            return Task.FromResult(new HttpResponseMessage(uploadStatus) { Content = new StringContent("sensitive-error-must-not-leak") });
        }
    }
    private static SupabaseAudioStorage Storage(Stub stub) => new(new HttpClient(stub), Options.Create(new SupabaseOptions
    { ProjectUrl = "https://fixture.supabase.co", Bucket = "audio", UploadKey = "sb_secret_fixture" }));

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
        Assert.Equal(1, stub.Uploads);
    }
    [Theory]
    [InlineData("http://fixture.supabase.co", "audio", "sb_secret_fixture")]
    [InlineData("https://elsewhere.test", "audio", "sb_secret_fixture")]
    [InlineData("https://fixture.supabase.co", "../audio", "sb_secret_fixture")]
    [InlineData("https://fixture.supabase.co", "audio", "sb_publishable_fixture")]
    public void Invalid_config_rejected_without_network(string url, string bucket, string key) =>
        Assert.False(new SupabaseOptions { ProjectUrl = url, Bucket = bucket, UploadKey = key }.IsConfigured);
}
