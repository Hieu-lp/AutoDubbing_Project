using System.Net;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AutoDubbing.Tests;

internal sealed record OrsRequest(string? Url, string Method, string? Authorization, string Body);

// Every test HTTP client uses this handler. It can never make an external network call.
internal sealed class OrsHttpStub : HttpMessageHandler
{
    public List<OrsRequest> Requests { get; } = [];
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Respond { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        Requests.Add(new OrsRequest(request.RequestUri?.AbsoluteUri, request.Method.Method,
            request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null, body));
        if (Respond is not null) { return await Respond(request, cancellationToken); }
        using var json = JsonDocument.Parse(body);
        var coordinates = json.RootElement.GetProperty("coordinates").EnumerateArray()
            .Select(position => position.EnumerateArray().Select(value => value.GetDouble()).ToArray()).ToArray();
        var segments = Enumerable.Range(1, coordinates.Length - 1).Select(index =>
        {
            var distance = coordinates[index].SequenceEqual(coordinates[index - 1]) ? 0d : index * 1000d;
            return new { distance, duration = distance / 2d };
        }).ToArray();
        var properties = new Dictionary<string, object>
        {
            ["summary"] = new { distance = segments.Sum(segment => segment.distance),
                duration = segments.Sum(segment => segment.duration) }
        };
        // Match ORS: disabling instructions also removes segment distance/duration entries.
        if (json.RootElement.GetProperty("instructions").GetBoolean()) { properties["segments"] = segments; }
        // Synthetic values strictly for tests; never a production fallback or road geometry.
        return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
        {
            type = "FeatureCollection", features = new[]
            {
                new
                {
                    type = "Feature", properties, geometry = new { type = "LineString", coordinates }
                }
            }
        }));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/geo+json")
    };
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
    public void Dispose() { }

    private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
    }
}
