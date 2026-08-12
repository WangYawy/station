using System.Collections.Concurrent;
using Station.Contracts.Alerts;
using Station.Contracts.Commands;
using Station.Contracts.Registration;
using Station.Contracts.Reporting;
using Station.Contracts.Sync;

namespace Station.Platform.Api.Data;

/// <summary>平台内存存储（M11 最小实现，三库持久化后续接入）。</summary>
public sealed class InMemoryPlatformStore
{
    private long _nextStationId = 1;

    public ConcurrentDictionary<long, StationRegistrationRequest> Stations { get; } = new();

    public ConcurrentDictionary<long, List<RemoteCommand>> CommandQueue { get; } = new();

    public ConcurrentDictionary<long, List<CommandExecutionResult>> CommandResults { get; } = new();

    public ConcurrentDictionary<long, long> ConfigVersions { get; } = new();

    public ConcurrentDictionary<long, List<ConfigChangeItem>> ConfigChanges { get; } = new();

    public List<FileMetadataReport> MetadataReports { get; } = [];

    public List<AlertReport> AlertReports { get; } = [];

    public long Register(StationRegistrationRequest request)
    {
        var id = Interlocked.Increment(ref _nextStationId);
        Stations[id] = request;
        CommandQueue[id] = [];
        CommandResults[id] = [];
        ConfigVersions[id] = 0;
        ConfigChanges[id] = [];
        return id;
    }
}
