using Microsoft.AspNetCore.SignalR;

namespace Station.Platform.Api.Realtime;

/// <summary>站↔平台实时事件（SignalR 广播给浏览器端，载荷仅含事件类型与归属，客户端按数据权限自行刷新）。</summary>
public sealed record StationRealtimeEvent(string Type, long? StationId, long? DeptId, DateTime OccurredAt);

/// <summary>实时事件类型常量。</summary>
public static class RealtimeEventTypes
{
    public const string StationRegistered = "station.registered";
    public const string FileReported = "file.reported";
    public const string AlertCreated = "alert.created";
    public const string CommandResult = "command.result";
    public const string LicenseStatus = "station.license";
    public const string StationStatus = "station.status";
    public const string RecorderWhitelist = "recorder.whitelist";
    public const string EmergencyUpdated = "emergency.updated";
}

/// <summary>实时事件总线：业务写入点发布，Hub 广播。</summary>
public interface IRealtimeEventBus
{
    Task PublishAsync(StationRealtimeEvent e);
}

public sealed class RealtimeEventBus : IRealtimeEventBus
{
    private readonly IHubContext<StationHub> _hub;

    public RealtimeEventBus(IHubContext<StationHub> hub)
    {
        _hub = hub;
    }

    public async Task PublishAsync(StationRealtimeEvent e) =>
        await _hub.Clients.All.SendAsync("station.event", e);
}

/// <summary>站↔平台实时推送 Hub（浏览器同源 + Cookie 认证；事件为全量广播，客户端按自身数据范围刷新）。</summary>
public sealed class StationHub : Hub
{
}
