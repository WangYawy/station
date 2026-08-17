using Station.Application.Collecting;

namespace Station.Desktop.Infrastructure.Collecting;

/// <summary>
/// 记录仪接入监听核心：按 SourceMode 检测设备，连续稳定 N 次轮询视为接入（等挂载稳定），
/// 去重处理；设备拔出后清除跟踪，再次插入可重新触发。模拟源模式下不工作。
/// </summary>
public sealed class RecorderConnectMonitor
{
    private readonly CollectOptions _options;
    private readonly IReadOnlyList<IRecorderDeviceDetector> _detectors;
    private readonly Func<DetectedDevice, Task> _handler;
    private readonly int _settlePolls;
    private readonly Dictionary<string, int> _stable = new(StringComparer.Ordinal);
    private readonly HashSet<string> _handled = new(StringComparer.Ordinal);

    public RecorderConnectMonitor(
        CollectOptions options,
        IReadOnlyList<IRecorderDeviceDetector> detectors,
        Func<DetectedDevice, Task> handler,
        int settlePolls = 3)
    {
        _options = options;
        _detectors = detectors;
        _handler = handler;
        _settlePolls = Math.Max(1, settlePolls);
    }

    public async Task CheckAsync()
    {
        if (_options.SourceMode == "simulated")
        {
            return;
        }

        if (_detectors.Count == 0)
        {
            return;
        }

        // 真实模式下同时监听 UMS 与 MTP：混合协议设备分别按各自协议处理
        var current = _detectors
            .SelectMany(d => d.Detect())
            .GroupBy(d => d.Key, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
        var keys = current.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (_handled.Contains(key))
            {
                continue;
            }

            _stable[key] = _stable.GetValueOrDefault(key) + 1;
            if (_stable[key] >= _settlePolls)
            {
                _handled.Add(key);
                var device = current.First(d => d.Key == key);
                try
                {
                    await _handler(device);
                }
                catch
                {
                    // 单设备处理失败不阻塞后续轮询
                }
            }
        }

        // 已拔出设备：清掉稳定计数与已处理标记，允许再次插入重新触发
        foreach (var key in _stable.Keys.Where(k => !keys.Contains(k)).ToList())
        {
            _stable.Remove(key);
            _handled.Remove(key);
        }
    }
}
