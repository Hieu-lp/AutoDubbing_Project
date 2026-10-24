using Microsoft.Extensions.Options;

namespace AutoDubbing.Service.ContentPreparation;

public interface IPreparationRateGate
{
    bool TryAcquire(string client, out int retryAfterSeconds);
}

// Shared by both POST endpoints. GET polling does not spend generation admission permits.
public sealed class PreparationRateGate(IOptions<ContentPreparationOptions> options, TimeProvider clock)
    : IPreparationRateGate
{
    private readonly object gate = new();
    private readonly Dictionary<string, int> clients = new();
    private DateTimeOffset windowEnd;
    private int total;

    public bool TryAcquire(string client, out int retryAfterSeconds)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            if (now >= windowEnd) { clients.Clear(); total = 0; windowEnd = now.AddMinutes(1); }
            retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((windowEnd - now).TotalSeconds));
            var count = clients.GetValueOrDefault(client);
            if (total >= options.Value.GlobalRequestsPerMinute || count >= options.Value.ClientRequestsPerMinute
                || (count == 0 && clients.Count >= 4096)) return false;
            clients[client] = count + 1;
            total++;
            return true;
        }
    }
}
