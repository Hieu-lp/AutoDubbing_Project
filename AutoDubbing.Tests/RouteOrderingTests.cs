using AutoDubbing.Data.Entities;
using AutoDubbing.Data.Repositories;
using AutoDubbing.Service.Routes;
using Microsoft.Extensions.Options;
using Xunit;

namespace AutoDubbing.Tests;

public sealed class RouteOrderingTests
{
    [Fact]
    public async Task Order_does_not_depend_on_repository_row_order_or_input_order_even_for_ties()
    {
        using var handler = new OrsHttpStub();
        using var client = new HttpClient(handler);
        var provider = new OrsDirectionsProvider(client,
            Options.Create(new OrsDirectionsOptions { ApiKey = "test-only-key" }));
        var service = new RoutePreviewService(new InputOrderedRepository(), provider);
        foreach (var ids in WalkingRouteApiTests.Permutations(1, 2, 3))
        {
            var result = await service.PreviewAsync(0, 0, ids, CancellationToken.None);
            Assert.Equal(new[] { 1, 3, 2 }, result.Preview!.Stops.Select(stop => stop.PoiId));
        }
        Assert.Single(handler.Requests.Select(request => request.Body).Distinct());
    }

    private sealed class InputOrderedRepository : IPoiReadRepository
    {
        private static readonly Dictionary<int, PointOfInterest> Pois = new()
        {
            [1] = new() { Id = 1, Name = "West fixture", Latitude = 0, Longitude = -1 },
            [2] = new() { Id = 2, Name = "East fixture", Latitude = 0, Longitude = 1 },
            [3] = new() { Id = 3, Name = "North fixture", Latitude = 1, Longitude = 0 }
        };
        public Task<List<PointOfInterest>> FindPublishedByIdsAsync(int[] ids, CancellationToken cancellationToken) =>
            Task.FromResult(ids.Select(id => Pois[id]).ToList());
        public Task<List<PointOfInterest>> ListPublishedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PointOfInterest?> FindPublishedAsync(int id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PoiTranslation?> FindReadyTranslationAsync(int id, string language, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PoiAudio?> FindReadyAudioAsync(int id, string language, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
