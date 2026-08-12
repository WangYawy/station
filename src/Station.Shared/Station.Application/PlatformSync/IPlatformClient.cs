using Station.Contracts.Alerts;
using Station.Contracts.Commands;
using Station.Contracts.Registration;
using Station.Contracts.Reporting;
using Station.Contracts.Sync;

namespace Station.Application.PlatformSync;

/// <summary>站→平台 HTTP 客户端（M1 契约）。</summary>
public interface IPlatformClient
{
    Task<StationRegistrationResponse?> RegisterAsync(StationRegistrationRequest request, CancellationToken ct);

    Task<ReportResult?> ReportMetadataAsync(FileMetadataReport report, CancellationToken ct);

    Task<bool> ReportAlertAsync(AlertReport report, CancellationToken ct);

    Task<ConfigSyncResponse?> SyncConfigAsync(ConfigSyncRequest request, CancellationToken ct);

    Task<IReadOnlyList<RemoteCommand>> PollCommandsAsync(long stationId, CancellationToken ct);

    Task<bool> ReportCommandResultAsync(long stationId, CommandExecutionResult result, CancellationToken ct);
}
