using Microsoft.AspNetCore.Mvc;
using Station.Contracts;
using Station.Contracts.Alerts;
using Station.Contracts.Api;
using Station.Contracts.Commands;
using Station.Contracts.Registration;
using Station.Contracts.Reporting;
using Station.Contracts.Sync;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Repositories;
using Station.Platform.Domain.Entities;

namespace Station.Platform.Api.Controllers;

/// <summary>站↔平台通信 API（M1 契约落地，三库持久化：MySQL/PostgreSQL/Kingbase 配置切换）。</summary>
[ApiController]
[Route(ApiRoutes.Base)]
public class StationCommunicationController : ControllerBase
{
    private readonly IRepository<PlatformStation> _stations;
    private readonly IRepository<PlatformFileMetadata> _files;
    private readonly IRepository<PlatformAlertReport> _alerts;
    private readonly IRepository<PlatformCommand> _commands;
    private readonly IIdGenerator _idGenerator;

    public StationCommunicationController(
        IRepository<PlatformStation> stations,
        IRepository<PlatformFileMetadata> files,
        IRepository<PlatformAlertReport> alerts,
        IRepository<PlatformCommand> commands,
        IIdGenerator idGenerator)
    {
        _stations = stations;
        _files = files;
        _alerts = alerts;
        _commands = commands;
        _idGenerator = idGenerator;
    }

    [HttpPost("stations/register")]
    public async Task<ActionResult<ApiResponse<StationRegistrationResponse>>> Register(
        StationRegistrationRequest request)
    {
        var existing = await _stations.FirstAsync(s => s.StationCode == request.StationCode);
        if (existing is not null)
        {
            return Ok(ApiResponse<StationRegistrationResponse>.Ok(ToResponse(existing)));
        }

        var station = new PlatformStation
        {
            Id = _idGenerator.NextId(),
            StationCode = request.StationCode,
            CpuSerial = request.MachineFingerprint.CpuSerial,
            MotherboardSerial = request.MachineFingerprint.MotherboardSerial,
            DiskSerial = request.MachineFingerprint.DiskSerial,
            MacAddress = request.MachineFingerprint.MacAddress,
            OsVersion = request.OsVersion,
            CpuArch = request.CpuArch,
            SoftwareVersion = request.SoftwareVersion,
            UsbPortCount = request.UsbPortCount,
            RegisteredAt = DateTime.Now
        };
        await _stations.InsertAsync(station);
        return Ok(ApiResponse<StationRegistrationResponse>.Ok(ToResponse(station)));
    }

    [HttpPost("stations/{stationId:long}/config-sync")]
    public ActionResult<ApiResponse<ConfigSyncResponse>> SyncConfig(long stationId, ConfigSyncRequest request) =>
        Ok(ApiResponse<ConfigSyncResponse>.Ok(new ConfigSyncResponse { Version = 0, Changes = [] }));

    [HttpPost("stations/{stationId:long}/files/metadata")]
    public async Task<ActionResult<ApiResponse<ReportResult>>> ReportMetadata(
        long stationId,
        FileMetadataReport report)
    {
        if (report.StationId != stationId)
        {
            return BadRequest(ApiResponse<ReportResult>.Fail(400, "StationId 不一致"));
        }

        var duplicate = await _files.IsAnyAsync(m =>
            m.StationId == stationId && m.LocalFileId == report.LocalFileId);
        if (!duplicate)
        {
            await _files.InsertAsync(new PlatformFileMetadata
            {
                Id = _idGenerator.NextId(),
                StationId = stationId,
                LocalFileId = report.LocalFileId,
                FileNo = report.FileNo,
                FileName = report.FileName,
                Size = report.Size,
                Kind = report.Kind,
                Sm3 = report.Sm3,
                CollectedAt = report.CollectedAt,
                RecorderSerial = report.RecorderSerial,
                UserNo = report.UserNo,
                DeptCode = report.DeptCode,
                StorageLocation = report.StorageLocation,
                ReceivedAt = DateTime.Now
            });
        }

        return Ok(ApiResponse<ReportResult>.Ok(new ReportResult(true, duplicate)));
    }

    [HttpPost("stations/{stationId:long}/alerts")]
    public async Task<ActionResult<ApiResponse<bool>>> ReportAlert(long stationId, AlertReport report)
    {
        await _alerts.InsertAsync(new PlatformAlertReport
        {
            Id = _idGenerator.NextId(),
            StationId = stationId,
            LocalAlertId = report.LocalAlertId,
            Type = report.Type,
            Level = report.Level,
            Source = report.Source,
            Message = report.Message,
            OccurredAt = report.OccurredAt,
            ReceivedAt = DateTime.Now
        });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpGet("stations/{stationId:long}/commands/poll")]
    public async Task<ActionResult<ApiResponse<List<RemoteCommand>>>> PollCommands(long stationId)
    {
        var pending = (await _commands.GetListAsync(c =>
                c.StationId == stationId && c.Status == CommandStatus.Pending))
            .OrderBy(c => c.Id)
            .Take(20)
            .ToList();
        foreach (var command in pending)
        {
            command.Status = CommandStatus.Pulled;
        }

        if (pending.Count > 0)
        {
            await _commands.UpdateRangeAsync(pending);
        }

        var result = pending.Select(c => new RemoteCommand
        {
            CommandId = c.Id,
            StationId = c.StationId,
            Type = c.Type,
            PayloadJson = c.PayloadJson,
            IssuedAt = c.IssuedAt,
            TimeoutSeconds = c.TimeoutSeconds,
            Signature = c.Signature
        }).ToList();
        return Ok(ApiResponse<List<RemoteCommand>>.Ok(result));
    }

    [HttpPost("stations/{stationId:long}/commands/{commandId:long}/result")]
    public async Task<ActionResult<ApiResponse<bool>>> ReportCommandResult(
        long stationId,
        long commandId,
        CommandExecutionResult result)
    {
        var command = await _commands.FirstAsync(c => c.Id == commandId && c.StationId == stationId);
        if (command is null)
        {
            return NotFound(ApiResponse<bool>.Fail(404, "指令不存在"));
        }

        command.Status = result.Status;
        command.ExecutedAt = result.FinishedAt ?? DateTime.Now;
        command.ResultMessage = result.Message;
        await _commands.UpdateAsync(command);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    private static StationRegistrationResponse ToResponse(PlatformStation station) => new()
    {
        StationId = station.Id,
        StationCode = station.StationCode,
        LicenseStatus = station.LicenseStatus,
        IsRegistered = station.IsRegistered,
        ConfigVersion = station.ConfigVersion
    };
}
