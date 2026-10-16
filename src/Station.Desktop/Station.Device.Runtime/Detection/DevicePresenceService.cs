using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Contracts;
using Station.Device.Detection;
using Station.Device.Events;
using Station.Device.Identity;
using Station.Device.Options;
using Station.Device.Port;

namespace Station.Device.Runtime.Detection;

/// <summary>
/// 设备在线聚合核心：按 SourceMode 过滤检测器，连续稳定 N 次轮询视为接入，
/// 去重处理；设备拔出后清除跟踪，再次插入可重新触发。
/// </summary>
/// <remarks>
/// 生命周期：单例。只负责物理层探测与事件广播；
/// 业务侧识别流程由 Application 层在 <see cref="IDeviceEventSink"/> 内完成。
/// </remarks>
public sealed class DevicePresenceService : IDevicePresenceService, IHostedService, IDisposable
{
    private readonly DeviceDetectionOptions _options;
    private readonly IReadOnlyList<IDeviceDetector> _detectors;
    private readonly IUsbPortResolver? _portResolver;
    private readonly IDeviceIdentityResolver _identity;
    private readonly IDeviceEventSink _events;
    private readonly ILogger<DevicePresenceService> _logger;
    private readonly int _settlePolls;

    private readonly ConcurrentDictionary<string, int> _stable = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DetectedDevice> _connected = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string?> _portCache = new(StringComparer.Ordinal);

    private Timer? _timer;
    private bool _disposed;

    public DevicePresenceService(
        IOptions<DeviceDetectionOptions> options,
        IEnumerable<IDeviceDetector> detectors,
        IDeviceIdentityResolver identity,
        IDeviceEventSink events,
        ILogger<DevicePresenceService> logger,
        IUsbPortResolver? portResolver = null)
    {
        _options = options.Value;
        _identity = identity;
        _events = events;
        _logger = logger;
        _portResolver = portResolver;

        var all = detectors.ToList();
        _detectors = _options.SourceMode switch
        {
            SourceMode.Ums => all.Where(d => d.Protocol == ProtocolType.Ums).ToList(),
            SourceMode.Mtp => all.Where(d => d.Protocol == ProtocolType.Mtp).ToList(),
            SourceMode.Mixed => all,
            SourceMode.Simulated => Array.Empty<IDeviceDetector>(),
            _ => all
        };

        _settlePolls = Math.Max(1, _options.SettlePolls);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(_ => Check(), null,
            TimeSpan.Zero, TimeSpan.FromSeconds(_options.PollIntervalSeconds));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        return Task.CompletedTask;
    }

    public IReadOnlyList<DetectedDevice> GetConnectedDevices() => _connected.Values.ToList();

    public Task RecheckAsync(string deviceKey, CancellationToken ct = default)
    {
        if (!_connected.TryGetValue(deviceKey, out var device))
            return Task.CompletedTask;

        // 重置链路态，重发物理接入事件（Application 层会重新走识别）
        var updated = device with { LinkState = DeviceLinkState.Verifying };
        _connected[deviceKey] = updated;

        _events.PublishConnected(new DeviceConnectedEvent(
            updated.Key, updated.Name, updated.Protocol, updated.Root, updated.PhysicalPortId));
        return Task.CompletedTask;
    }

    /// <summary>单轮检测。</summary>
    public void Check()
    {
        if (_options.SourceMode == SourceMode.Simulated) return;
        if (_detectors.Count == 0) return;

        List<DetectedDevice> raw;
        try { raw = _detectors.SelectMany(d => d.Detect()).ToList(); }
        catch (Exception ex) { _logger.LogError(ex, "设备检测器执行异常"); return; }

        if (raw.Count == 0)
        {
            var allConnected = _connected.Values.ToList();
            _stable.Clear();
            _connected.Clear();
            foreach (var device in allConnected)
                TriggerDisconnect(device);
            return;
        }

        // 按 Name 分组，多协议选优选（UMS > MTP > PrivateSdk）
        var groups = raw.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase);
        var current = new List<DetectedDevice>();
        foreach (var group in groups)
        {
            DetectedDevice selected = group.Count() == 1
                ? group.First()
                : group.OrderBy(d => d.Protocol == ProtocolType.Ums ? 0 :
                                     d.Protocol == ProtocolType.Mtp ? 1 : 2)
                       .First();

            selected = EnrichDevice(selected);
            current.Add(selected);
        }

        var keys = current.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            if (_connected.ContainsKey(key)) continue;

            var newCount = _stable.AddOrUpdate(key, 1, (_, old) => old + 1);
            if (newCount < _settlePolls) continue;

            var device = current.First(d => d.Key == key) with { LinkState = DeviceLinkState.Verifying };
            if (_connected.TryAdd(key, device))
            {
                _events.PublishConnected(new DeviceConnectedEvent(
                    device.Key, device.Name, device.Protocol, device.Root, device.PhysicalPortId));
            }
        }

        // 清理已拔出
        foreach (var key in _stable.Keys.Where(k => !keys.Contains(k)).ToList())
        {
            _stable.TryRemove(key, out _);
            if (_connected.TryRemove(key, out var disconnected))
                TriggerDisconnect(disconnected);
        }
    }

    // ---------- 内部 ----------

    private DetectedDevice EnrichDevice(DetectedDevice device)
    {
        string? portId = null;
        if (_portResolver is { IsAvailable: true })
        {
            if (!_portCache.TryGetValue(device.Root, out portId))
            {
                portId = _portResolver.ResolvePhysicalPortId(device.Root);
                _portCache[device.Root] = portId;
            }
        }

        var enriched = device with { PhysicalPortId = portId };
        return enriched with { Key = _identity.Resolve(enriched) };
    }

    private void TriggerDisconnect(DetectedDevice device)
    {
        _portCache.TryRemove(device.Root, out _);
        try
        {
            _events.PublishDisconnected(new DeviceDisconnectedEvent(
                device.Key, device.Name, device.PhysicalPortId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "断开事件广播异常：{Device}", device.Name);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _timer?.Dispose();
        _disposed = true;
    }
}
