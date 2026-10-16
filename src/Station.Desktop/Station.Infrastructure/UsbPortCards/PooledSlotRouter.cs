using System.Collections.Concurrent;
using Station.Application.UsbPortCards;

namespace Station.Infrastructure.UsbPortCards;

/// <summary>空闲模式：池化分配，先到先占；释放归还队列。</summary>
public sealed class PooledSlotRouter : ISlotRouter
{
    private readonly ConcurrentDictionary<string, int> _deviceKeyToSlot = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, int> _taskIdToSlot = new();
    private readonly ConcurrentQueue<int> _freeSlots;

    public PooledSlotRouter(int capacity)
    {
        Capacity = capacity;
        _freeSlots = new ConcurrentQueue<int>(Enumerable.Range(0, capacity));
    }

    public SlotMode Mode => SlotMode.Pooled;
    public int Capacity { get; }

    public int? ResolveSlotForDevice(string deviceKey)
    {
        if (string.IsNullOrEmpty(deviceKey)) return null;
        if (_deviceKeyToSlot.TryGetValue(deviceKey, out var cached)) return cached;

        if (!_freeSlots.TryDequeue(out var slot)) return null;
        _deviceKeyToSlot[deviceKey] = slot;
        return slot;
    }

    public int? ResolveSlotForTask(long taskId)
        => _taskIdToSlot.TryGetValue(taskId, out var s) ? s : null;

    public void BindTask(long taskId, int slot) => _taskIdToSlot[taskId] = slot;

    public void ReleaseSlot(int slot)
    {
        var removed = false;
        foreach (var kv in _deviceKeyToSlot.Where(kv => kv.Value == slot).ToList())
            if (_deviceKeyToSlot.TryRemove(kv.Key, out _)) removed = true;
        foreach (var kv in _taskIdToSlot.Where(kv => kv.Value == slot).ToList())
            if (_taskIdToSlot.TryRemove(kv.Key, out _)) removed = true;

        if (removed) _freeSlots.Enqueue(slot);
    }

    public void Reset()
    {
        _deviceKeyToSlot.Clear();
        _taskIdToSlot.Clear();
        while (_freeSlots.TryDequeue(out _)) { }
        for (var i = 0; i < Capacity; i++) _freeSlots.Enqueue(i);
    }

    public IReadOnlyDictionary<int, string> GetSlotBindings()
        => _deviceKeyToSlot.ToDictionary(kv => kv.Value, kv => kv.Key);
}
