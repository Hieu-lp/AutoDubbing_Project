using System.Net;
using AutoDubbing.Data.Entities;
using AutoDubbing.Data.Repositories;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AutoDubbing.Tests;

public sealed class PublicErrorTests
{
    [Fact]
    public async Task Unexpected_failure_returns_generic_problem_without_exception_or_connection_details()
    {
        await using var originalFactory = new AdminApiFactory();
        await using var factory = originalFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddScoped<IPoiReadRepository, FailingRepository>()));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        using var response = await client.GetAsync("/api/pois");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SECRET_MARKER", content);
        Assert.DoesNotContain("InvalidOperationException", content);
        Assert.DoesNotContain("StackTrace", content, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private sealed class FailingRepository : IPoiReadRepository
    {
        public Task<List<PointOfInterest>> ListPublishedAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("SECRET_MARKER: connection details must stay on the server.");
        public Task<PointOfInterest?> FindPublishedAsync(int id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<List<PointOfInterest>> FindPublishedByIdsAsync(int[] ids, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PoiTranslation?> FindReadyTranslationAsync(int id, string language, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PoiAudio?> FindReadyAudioAsync(int id, string language, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
