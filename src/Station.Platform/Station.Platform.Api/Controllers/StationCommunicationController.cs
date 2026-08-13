using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Station.Contracts;
using Station.Contracts.Alerts;
using Station.Contracts.Api;
using Station.Contracts.Commands;
using Station.Contracts.Registration;
using Station.Contracts.Reporting;
using Station.Contracts.Sync;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;
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
    private readonly IRepository<PlatformConfigChange> _configChanges;
    private readonly IConfiguration _configuration;
    private readonly IIdGenerator _idGenerator;

    public StationCommunicationController(
        IRepository<PlatformStation> stations,
        IRepository<PlatformFileMetadata> files,
        IRepository<PlatformAlertReport> alerts,
        IRepository<PlatformCommand> commands,
        IRepository<PlatformConfigChange> configChanges,
        IIdGenerator idGenerator,
        IConfiguration configuration)
    {
        _stations = stations;
        _files = files;
        _alerts = alerts;
        _commands = commands;
        _configChanges = configChanges;
        _idGenerator = idGenerator;
        _configuration = configuration;
    }

    [HttpPost("stations/register")]
    public async Task<ActionResult<ApiResponse<StationRegistrationResponse>>> Register(
        StationRegistrationRequest request)
    {
        var existing = await _stations.FirstAsync(s => s.StationCode == request.StationCode);
        if (existing is not null)
        {
            existing.LastHeartbeatAt = DateTime.Now;
            await _stations.UpdateAsync(existing);
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
            StationBaseUrl = request.StationBaseUrl,
            LastHeartbeatAt = DateTime.Now,
            RegisteredAt = DateTime.Now
        };
        await _stations.InsertAsync(station);
        return Ok(ApiResponse<StationRegistrationResponse>.Ok(ToResponse(station)));
    }

    [HttpPost("stations/{stationId:long}/config-sync")]
    public async Task<ActionResult<ApiResponse<ConfigSyncResponse>>> SyncConfig(
        long stationId,
        ConfigSyncRequest request)
    {
        await TouchHeartbeatAsync(stationId);
        var changes = (await _configChanges.GetListAsync(c => c.StationId == stationId)).ToList();
        var pending = changes
            .Where(c => !request.AppliedVersions.TryGetValue(c.EntityType, out var applied) || c.Version > applied)
            .OrderBy(c => c.Version)
            .Select(PlatformConfigsController.ToItem)
            .ToList();
        var version = changes.Count == 0 ? 0 : changes.Max(c => c.Version);
        return Ok(ApiResponse<ConfigSyncResponse>.Ok(new ConfigSyncResponse
        {
            Version = version,
            Changes = pending
        }));
    }

    [HttpPost("stations/{stationId:long}/files/metadata")]
    public async Task<ActionResult<ApiResponse<ReportResult>>> ReportMetadata(
        long stationId,
        FileMetadataReport report)
    {
        await TouchHeartbeatAsync(stationId);
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
                DeptId = (await _stations.GetByIdAsync(stationId))?.DeptId,
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
        await TouchHeartbeatAsync(stationId);
        var publicKey = ReadReportingPublicKey();
        if (!string.IsNullOrWhiteSpace(publicKey) &&
            !Sm2LicenseSigner.Verify(publicKey, AlertReportSignature.Canonical(report), report.Signature ?? string.Empty))
        {
            return BadRequest(ApiResponse<bool>.Fail(400, "报警签名无效"));
        }

        await _alerts.InsertAsync(new PlatformAlertReport
        {
            Id = _idGenerator.NextId(),
            StationId = stationId,
            DeptId = (await _stations.GetByIdAsync(stationId))?.DeptId,
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

    [HttpPost("stations/{stationId:long}/license")]
    public async Task<ActionResult<ApiResponse<bool>>> ReportLicenseStatus(
        long stationId,
        Station.Contracts.Reporting.LicenseStatusReport report)
    {
        var publicKey = ReadReportingPublicKey();
        if (!string.IsNullOrWhiteSpace(publicKey) &&
            !Sm2LicenseSigner.Verify(publicKey, LicenseStatusReportSignature.Canonical(report), report.Signature ?? string.Empty))
        {
            return BadRequest(ApiResponse<bool>.Fail(400, "授权状态签名无效"));
        }

        var station = await _stations.GetByIdAsync(stationId);
        if (station is null)
        {
            return NotFound(ApiResponse<bool>.Fail(404, "采集站未注册"));
        }

        station.LicenseStatus = report.LicenseStatus;
        station.LicenseExpiresAt = report.ExpiresAt;
        station.LicenseDaysLeft = report.DaysLeft;
        station.LastHeartbeatAt = DateTime.Now;
        await _stations.UpdateAsync(station);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    private string? ReadReportingPublicKey()
    {
        var publicKey = _configuration["Platform:Reporting:PublicKeyPem"];
        if (string.IsNullOrWhiteSpace(publicKey) &&
            _configuration["Platform:Reporting:PublicKeyPemFile"] is { } pemFile &&
            System.IO.File.Exists(pemFile))
        {
            publicKey = System.IO.File.ReadAllText(pemFile).Trim();
        }

        return publicKey;
    }

    [HttpGet("stations/{stationId:long}/commands/poll")]
    public async Task<ActionResult<ApiResponse<List<RemoteCommand>>>> PollCommands(long stationId)
    {
        await TouchHeartbeatAsync(stationId);
        var publicKey = PlatformCommandKeys.ReadPublicKey(_configuration);
        var pending = (await _commands.GetListAsync(c =>
            c.StationId == stationId && c.Status == CommandStatus.Pending))
            .OrderBy(c => c.Id)
            .Take(20)
            .ToList();

        var accepted = new List<PlatformCommand>();
        var acceptedRemotes = new List<RemoteCommand>();
        var rejected = new List<PlatformCommand>();
        foreach (var command in pending)
        {
            var remote = ToRemoteCommand(command);
            if (VerifyCommandAtRead(publicKey, remote) is { } reason)
            {
                command.Status = CommandStatus.Failed;
                command.ExecutedAt = DateTime.Now;
                command.ResultMessage = reason;
                rejected.Add(command);
                continue;
            }

            command.Status = CommandStatus.Pulled;
            accepted.Add(command);
            acceptedRemotes.Add(remote);
        }

        if (accepted.Count > 0)
        {
            await _commands.UpdateRangeAsync(accepted);
        }

        if (rejected.Count > 0)
        {
            await _commands.UpdateRangeAsync(rejected);
        }

        return Ok(ApiResponse<List<RemoteCommand>>.Ok(acceptedRemotes));
    }

    [HttpPost("stations/{stationId:long}/commands/{commandId:long}/result")]
    public async Task<ActionResult<ApiResponse<bool>>> ReportCommandResult(
        long stationId,
        long commandId,
        CommandExecutionResult result)
    {
        await TouchHeartbeatAsync(stationId);
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

    private async Task TouchHeartbeatAsync(long stationId)
    {
        var station = await _stations.GetByIdAsync(stationId);
        if (station is null)
        {
            return;
        }

        station.LastHeartbeatAt = DateTime.Now;
        await _stations.UpdateAsync(station);
    }

    /// <summary>读取时实时验签：未配置公钥（开发模式）跳过；未签名或验签失败返回拒绝原因。</summary>
    private static string? VerifyCommandAtRead(string? publicKey, RemoteCommand remote)
    {
        if (publicKey is null)
        {
            return null;
        }

        if (remote.Signature == "unsigned")
        {
            return "未签名指令（未配置签名或已被篡改），拒绝下发";
        }

        return Sm2LicenseSigner.Verify(
            publicKey,
            RemoteCommandSignature.Canonical(remote),
            remote.Signature)
            ? null
            : "指令签名校验失败（疑似被篡改），拒绝下发";
    }

    private static RemoteCommand ToRemoteCommand(PlatformCommand c) => new()
    {
        CommandId = c.Id,
        StationId = c.StationId,
        Type = c.Type,
        PayloadJson = c.PayloadJson,
        IssuedAt = c.IssuedAt,
        TimeoutSeconds = c.TimeoutSeconds,
        Signature = c.Signature
    };
}
