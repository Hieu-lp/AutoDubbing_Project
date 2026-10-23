using System.Collections.Concurrent;

namespace AutoDubbing.Service.ContentGeneration;

// Một instance backend; khóa theo stripe có kích thước cố định, không tăng bộ nhớ theo ID.
public sealed class GenerationCoordinator
{
    private readonly SemaphoreSlim[] poiLocks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly ConcurrentDictionary<string, DateTimeOffset> cooldowns = new();
    public SemaphoreSlim Slots { get; } = new(2, 2);
    public SemaphoreSlim PoiLock(int id) => poiLocks[(int)((uint)id % (uint)poiLocks.Length)];
    public bool IsCooling(string model) => cooldowns.TryGetValue(model, out var until) && until > DateTimeOffset.UtcNow;
    public void Pause(string model, TimeSpan duration) => cooldowns[model] = DateTimeOffset.UtcNow + duration;
}
