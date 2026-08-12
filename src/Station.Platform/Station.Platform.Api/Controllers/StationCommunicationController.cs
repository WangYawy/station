using Microsoft.AspNetCore.Mvc;
using Station.Contracts;
using Station.Contracts.Alerts;
using Station.Contracts.Api;
using Station.Contracts.Commands;
using Station.Contracts.Registration;
using Station.Contracts.Reporting;
using Station.Contracts.Sync;
using Station.Platform.Api.Data;

namespace Station.Platform.Api.Controllers;

/// <summary>站↔平台通信 API（M1 契约落地，内存存储）。</summary>
[ApiController]
[Route(ApiRoutes.Base)]
public class StationCommunicationController : ControllerBase
{
    private readonly InMemoryPlatformStore _store;

    public StationCommunicationController(InMemoryPlatformStore store)
    {
        _store = store;
    }

    [HttpPost("stations/register")]
    public ActionResult<ApiResponse<StationRegistrationResponse>> Register(StationRegistrationRequest request)
    {
        var stationId = _store.Register(request);
        return Ok(ApiResponse<StationRegistrationResponse>.Ok(new StationRegistrationResponse
        {
            StationId = stationId,
            StationCode = request.StationCode,
            LicenseStatus = LicenseStatus.Trial,
            IsRegistered = true,
            ConfigVersion = 0
        }));
    }

    [HttpPost("stations/{stationId:long}/config-sync")]
    public ActionResult<ApiResponse<ConfigSyncResponse>> SyncConfig(long stationId, ConfigSyncRequest request)
    {
        if (!_store.Stations.ContainsKey(stationId))
        {
            return NotFound(ApiResponse<ConfigSyncResponse>.Fail(404, "采集站未注册"));
        }

        var version = _store.ConfigVersions[stationId];
        var changes = _store.ConfigChanges[stationId];
        return Ok(ApiResponse<ConfigSyncResponse>.Ok(new ConfigSyncResponse
        {
            Version = version,
            Changes = changes
        }));
    }

    [HttpPost("stations/{stationId:long}/files/metadata")]
    public ActionResult<ApiResponse<ReportResult>> ReportMetadata(long stationId, FileMetadataReport report)
    {
        if (report.StationId != stationId)
        {
            return BadRequest(ApiResponse<ReportResult>.Fail(400, "StationId 不一致"));
        }

        var duplicate = _store.MetadataReports.Any(m => m.StationId == stationId && m.LocalFileId == report.LocalFileId);
        if (!duplicate)
        {
            _store.MetadataReports.Add(report);
        }

        return Ok(ApiResponse<ReportResult>.Ok(new ReportResult(true, duplicate)));
    }

    [HttpPost("stations/{stationId:long}/alerts")]
    public ActionResult<ApiResponse<bool>> ReportAlert(long stationId, AlertReport report)
    {
        _store.AlertReports.Add(report);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("stations/{stationId:long}/commands/poll")]
    public ActionResult<ApiResponse<List<RemoteCommand>>> PollCommands(long stationId)
    {
        if (!_store.CommandQueue.TryGetValue(stationId, out var queue))
        {
            return NotFound(ApiResponse<List<RemoteCommand>>.Fail(404, "采集站未注册"));
        }

        var commands = new List<RemoteCommand>(queue);
        queue.Clear();
        return Ok(ApiResponse<List<RemoteCommand>>.Ok(commands));
    }

    [HttpPost("stations/{stationId:long}/commands/{commandId:long}/result")]
    public ActionResult<ApiResponse<bool>> ReportCommandResult(long stationId, long commandId, CommandExecutionResult result)
    {
        if (!_store.CommandResults.TryGetValue(stationId, out var results))
        {
            return NotFound(ApiResponse<bool>.Fail(404, "采集站未注册"));
        }

        results.Add(result);
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
