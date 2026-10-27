using System.Collections.Concurrent;

namespace AutoDubbing.Service.ContentGeneration;

// Một instance backend; khóa theo stripe có kích thước cố định, không tăng bộ nhớ theo ID.
public sealed class GenerationCoordinator
{
    private readonly SemaphoreSlim[] poiLocks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly ConcurrentDictionary<string, (DateTimeOffset Until, GenerationFailureDetails? Details)> cooldowns = new();
    public SemaphoreSlim Slots { get; } = new(1, 1);
    public SemaphoreSlim PoiLock(int id) => poiLocks[(int)((uint)id % (uint)poiLocks.Length)];
    public bool IsCooling(string model) => cooldowns.TryGetValue(model, out var value) && value.Until > DateTimeOffset.UtcNow;
    public GenerationFailureDetails? CoolingFailure(string model) =>
        IsCooling(model) && cooldowns.TryGetValue(model, out var value) ? value.Details : null;
    public void Pause(string model, TimeSpan duration, GenerationFailureDetails? details = null) =>
        cooldowns[model] = (DateTimeOffset.UtcNow + duration, details);
}
