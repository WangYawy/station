using System.Collections.Concurrent;
using Station.Application.UsbPortCards;

namespace Station.Infrastructure.UsbPortCards;

/// <summary>
/// 固定模式：卡片与物理端口 1:1。deviceKey 必须携带 <c>PORT:</c> 前缀，
/// 内部提取 PhysicalPortId 查映射表。
/// </summary>
public sealed class FixedSlotRouter : ISlotRouter
{
    private const string PortPrefix = "PORT:";

    private readonly IReadOnlyDictionary<string, int> _portToSlot;
    private readonly ConcurrentDictionary<string, int> _deviceKeyToSlot = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, int> _taskIdToSlot = new();

    public FixedSlotRouter(IReadOnlyDictionary<string, int> portToSlot, int capacity)
    {
        // 归一化 key：去空格 + 大写，避免大小写 / 前后空格导致漏匹配
        var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in portToSlot)
            dict[Normalize(kv.Key)] = kv.Value - 1;    // 1-based → 0-based

        _portToSlot = dict;
        Capacity = capacity;
    }

    public SlotMode Mode => SlotMode.Fixed;
    public int Capacity { get; }

    public int? ResolveSlotForDevice(string deviceKey)
    {
        if (string.IsNullOrEmpty(deviceKey)) return null;
        if (_deviceKeyToSlot.TryGetValue(deviceKey, out var cached)) return cached;

        // 从 DeviceKey 中提取 PhysicalPortId（"PORT:WIN:HUB0001:PORT0003"）
        if (!deviceKey.StartsWith(PortPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var portId = deviceKey[PortPrefix.Length..];
        if (!_portToSlot.TryGetValue(Normalize(portId), out var slot))
            return null;

        _deviceKeyToSlot[deviceKey] = slot;
        return slot;
    }

    public int? ResolveSlotForTask(long taskId)
        => _taskIdToSlot.TryGetValue(taskId, out var s) ? s : null;

    public void BindTask(long taskId, int slot) => _taskIdToSlot[taskId] = slot;

    public void ReleaseSlot(int slot)
    {
        foreach (var kv in _deviceKeyToSlot.Where(kv => kv.Value == slot).ToList())
            _deviceKeyToSlot.TryRemove(kv.Key, out _);
        foreach (var kv in _taskIdToSlot.Where(kv => kv.Value == slot).ToList())
            _taskIdToSlot.TryRemove(kv.Key, out _);
    }

    public void Reset()
    {
        _deviceKeyToSlot.Clear();
        _taskIdToSlot.Clear();
    }

    public IReadOnlyDictionary<int, string> GetSlotBindings()
        => _deviceKeyToSlot.ToDictionary(kv => kv.Value, kv => kv.Key);

    private static string Normalize(string s) => s.Trim().ToUpperInvariant();
}
