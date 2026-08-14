using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using Station.Contracts;
using Station.Contracts.Api;
using Station.Application.Audit;
using AppAuthorization = Station.Application.Authorization.IAuthorizationService;
using Station.Application.Authorization;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure.Repositories;
using Station.Platform.Domain.Entities;
using Station.Platform.Api.Realtime;
using Station.Infrastructure.Persistence;

namespace Station.Platform.Api.Controllers;

/// <summary>平台报警/采集站授权管理查询。</summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public class PlatformAdminController : ControllerBase
{
    private readonly IRepository<PlatformAlertReport> _alerts;
    private readonly IRepository<PlatformStation> _stations;
    private readonly IRepository<PlatformFileMetadata> _files;
    private readonly IRepository<PlatformRecorder> _recorders;
    private readonly IRepository<Dept> _depts;
    private readonly IAuditLogService _audit;
    private readonly AppAuthorization _authorization;
    private readonly IDataScopeProvider _dataScope;
    private readonly IRealtimeEventBus _realtime;
    private readonly int _onlineTimeoutSeconds;

    public PlatformAdminController(
        IRepository<PlatformAlertReport> alerts,
        IRepository<PlatformStation> stations,
        IRepository<PlatformFileMetadata> files,
        IRepository<PlatformRecorder> recorders,
        IRepository<Dept> depts,
        IAuditLogService audit,
        AppAuthorization authorization,
        IDataScopeProvider dataScope,
        IRealtimeEventBus realtime,
        IConfiguration configuration)
    {
        _alerts = alerts;
        _stations = stations;
        _files = files;
        _recorders = recorders;
        _depts = depts;
        _audit = audit;
        _authorization = authorization;
        _dataScope = dataScope;
        _realtime = realtime;
        _onlineTimeoutSeconds = configuration.GetValue("Platform:OnlineTimeoutSeconds", 300);
    }

    [HttpGet("alerts")]
    public async Task<ActionResult<ApiResponse<PagedResult<AlertView>>>> ListAlerts(
        [FromQuery] long? stationId,
        [FromQuery] AlertLevel? level,
        [FromQuery] AlertStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20)
    {
        var scope = await GetScopeAsync();
        var query = _alerts.AsQueryable()
            .Where(a =>
                (stationId == null || a.StationId == stationId) &&
                (level == null || a.Level == level) &&
                (status == null || a.Status == status));
        if (!scope.IsAll)
        {
            query = query.Where(a => a.DeptId != null && scope.AllowedDeptIds.Contains(a.DeptId.Value));
        }

        var total = query.Count();
        var items = query.OrderBy(a => a.OccurredAt, OrderByType.Desc)
            .ToPageList(Math.Max(1, page), Math.Max(1, size));
        return Ok(ApiResponse<PagedResult<AlertView>>.Ok(new PagedResult<AlertView>(
            page, size, total, items.Select(a => new AlertView(
                a.Id, a.StationId, a.DeptId, a.Type, a.Level, a.Status, a.Source, a.Message, a.OccurredAt, a.ReceivedAt)).ToList())));
    }

    [HttpGet("stations")]
    public async Task<ActionResult<ApiResponse<PagedResult<StationView>>>> ListStations(
        [FromQuery] int page = 1,
        [FromQuery] int size = 50)
    {
        var scope = await GetScopeAsync();
        var query = _stations.AsQueryable();
        if (!scope.IsAll)
        {
            query = query.Where(s => s.DeptId != null && scope.AllowedDeptIds.Contains(s.DeptId.Value));
        }

        var total = query.Count();
        var items = query.OrderBy(s => s.Id, OrderByType.Asc)
            .ToPageList(Math.Max(1, page), Math.Max(1, size));
        return Ok(ApiResponse<PagedResult<StationView>>.Ok(new PagedResult<StationView>(
            page, size, total, items.Select(s => new StationView(
                s.Id, s.StationCode, s.OsVersion, s.CpuArch, s.SoftwareVersion,
                s.OperationalStatus, s.LicenseStatus, s.LicenseExpiresAt, s.LicenseDaysLeft, s.DeptId, s.RegisteredAt)).ToList())));
    }

    /// <summary>采集站运行状态（正常/维修/报废），station:manage + 数据范围。</summary>
    [HttpPut("stations/{stationId:long}/status")]
    public async Task<IActionResult> SetStationStatus(long stationId, [FromBody] SetStationStatusRequest request)
    {
        if (User.FindFirst("accountId") is not { } accountClaim ||
            !await _authorization.HasPermissionAsync(long.Parse(accountClaim.Value), PermissionCodes.StationManage))
        {
            return StatusCode(403, new { message = "无采集站管理权限" });
        }

        var station = await _stations.GetByIdAsync(stationId);
        if (station is null)
        {
            return NotFound(new { message = "采集站不存在" });
        }

        var scope = await GetScopeAsync();
        if (!scope.IsAll && (station.DeptId is null || !scope.AllowedDeptIds.Contains(station.DeptId.Value)))
        {
            return StatusCode(403, new { message = "无权管理该采集站" });
        }

        station.OperationalStatus = request.Status;
        await _stations.UpdateAsync(station);
        await WriteAuditAsync("station.status", station.StationCode, $"设置运行状态 {request.Status}");
        await _realtime.PublishAsync(new StationRealtimeEvent(
            RealtimeEventTypes.StationStatus, station.Id, station.DeptId, DateTime.Now));
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>报警处置：确认/处理/关闭（需要 alert:handle 权限）。</summary>
    [HttpPost("alerts/{alertId:long}/status")]
    public async Task<IActionResult> SetAlertStatus(long alertId, [FromBody] SetAlertStatusRequest request)
    {
        if (User.FindFirst("accountId") is not { } accountClaim ||
            !await _authorization.HasPermissionAsync(long.Parse(accountClaim.Value), "alert:handle"))
        {
            return StatusCode(403, new { message = "无报警处置权限" });
        }

        var alert = await _alerts.GetByIdAsync(alertId);
        if (alert is null)
        {
            return NotFound(new { message = "报警不存在" });
        }

        alert.Status = request.Status;
        await _alerts.UpdateAsync(alert);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>部门树（用于采集站归属调整等组织数据展示）。</summary>
    [HttpGet("depts")]
    public async Task<IActionResult> ListDepts()
    {
        if (User.FindFirst("accountId") is not { } accountClaim ||
            !await _authorization.HasPermissionAsync(long.Parse(accountClaim.Value), PermissionCodes.DeptView))
        {
            return StatusCode(403, new { message = "无部门查看权限" });
        }

        var scope = await GetScopeAsync();
        var depts = await _depts.GetListAsync(d => d.IsActive);
        if (!scope.IsAll)
        {
            depts = depts.Where(d => scope.AllowedDeptIds.Contains(d.Id)).ToList();
        }

        return Ok(ApiResponse<List<DeptView>>.Ok(depts
            .OrderBy(d => d.SortOrder)
            .Select(d => new DeptView(d.Id, d.Code, d.Name, d.ParentId, d.SortOrder))
            .ToList()));
    }

    /// <summary>调整采集站部门归属（station:manage；历史文件/报警保留上报时归属快照）。</summary>
    [HttpPut("stations/{stationId:long}/dept")]
    public async Task<IActionResult> UpdateStationDept(long stationId, [FromBody] UpdateStationDeptRequest request)
    {
        if (User.FindFirst("accountId") is not { } accountClaim ||
            !await _authorization.HasPermissionAsync(long.Parse(accountClaim.Value), PermissionCodes.StationManage))
        {
            return StatusCode(403, new { message = "无采集站管理权限" });
        }

        var station = await _stations.GetByIdAsync(stationId);
        if (station is null)
        {
            return NotFound(new { message = "采集站不存在" });
        }

        var scope = await GetScopeAsync();
        if (!scope.IsAll && (station.DeptId is null || !scope.AllowedDeptIds.Contains(station.DeptId.Value)))
        {
            return StatusCode(403, new { message = "无权调整该采集站归属" });
        }

        if (request.DeptId is { } deptId)
        {
            var dept = await _depts.GetByIdAsync(deptId);
            if (dept is null || !dept.IsActive)
            {
                return BadRequest(new { message = "部门不存在或已停用" });
            }
        }

        station.DeptId = request.DeptId;
        await _stations.UpdateAsync(station);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    /// <summary>
    /// 采集站详情：基础信息 + 文件/报警/记录仪聚合 + 存储位置分布（数据范围过滤）。
    /// </summary>
    [HttpGet("stations/{stationId:long}")]
    public async Task<IActionResult> GetStationDetail(long stationId)
    {
        var station = await _stations.GetByIdAsync(stationId);
        if (station is null)
        {
            return NotFound(new { message = "采集站不存在" });
        }

        var scope = await GetScopeAsync();
        if (!scope.IsAll && (station.DeptId is null || !scope.AllowedDeptIds.Contains(station.DeptId.Value)))
        {
            return StatusCode(403, new { message = "无权查看该采集站" });
        }

        var onlineCutoff = DateTime.Now.AddSeconds(-_onlineTimeoutSeconds);
        var isOnline = station.LastHeartbeatAt != null && station.LastHeartbeatAt >= onlineCutoff;

        var fileRows = await _files.GetListAsync(f => f.StationId == stationId);
        var alertRows = await _alerts.GetListAsync(a => a.StationId == stationId);
        var recorderRows = await _recorders.GetListAsync(r => r.LastStationId == stationId && r.IsActive);
        var deptName = station.DeptId is { } deptId
            ? (await _depts.GetByIdAsync(deptId))?.Name
            : null;

        var todayStart = DateTime.Today;
        var storageUsage = fileRows
            .Where(f => !string.IsNullOrWhiteSpace(f.StorageLocation))
            .GroupBy(f => f.StorageLocation!)
            .Select(g => new StorageUsageView(g.Key, g.LongCount(), g.Sum(x => x.Size)))
            .OrderByDescending(x => x.TotalSize)
            .ToList();

        var detail = new StationDetailView(
            station.Id,
            station.StationCode,
            station.OsVersion,
            station.CpuArch,
            station.SoftwareVersion,
            station.OperationalStatus,
            station.LicenseStatus,
            station.LicenseExpiresAt,
            station.LicenseDaysLeft,
            station.DeptId,
            station.RegisteredAt,
            deptName,
            station.CpuSerial,
            station.MotherboardSerial,
            station.DiskSerial,
            station.MacAddress,
            station.UsbPortCount,
            station.ConfigVersion,
            station.StationBaseUrl,
            station.LastHeartbeatAt,
            isOnline,
            fileRows.LongCount(),
            fileRows.Sum(f => f.Size),
            fileRows.Count(f => f.CollectedAt >= todayStart),
            fileRows.Where(f => f.CollectedAt >= todayStart).Sum(f => f.Size),
            alertRows.Count(a => a.Status == AlertStatus.Pending),
            alertRows.Where(a => a.Status == AlertStatus.Pending)
                .GroupBy(a => a.Level)
                .Select(g => new CountItemView(((int)g.Key).ToString(), g.LongCount()))
                .OrderBy(x => int.Parse(x.Key))
                .ToList(),
            recorderRows.Count,
            recorderRows.Count(r => r.IsWhitelisted),
            storageUsage);
        return Ok(ApiResponse<StationDetailView>.Ok(detail));
    }

    private async Task<DataScopeResult> GetScopeAsync()
    {
        return await DataScopeHelper.GetScopeAsync(User, _authorization, _dataScope);
    }

    private async Task WriteAuditAsync(string operationType, string? target, string? detail)
    {
        await _audit.WriteAsync(new AuditLog
        {
            OperatorAccount = User.Identity?.Name,
            OperationType = operationType,
            Target = target,
            Detail = detail,
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Result = 1
        });
    }
}

    public sealed record SetAlertStatusRequest(AlertStatus Status);

    public sealed record SetStationStatusRequest(StationOperationalStatus Status);

    public sealed record UpdateStationDeptRequest(long? DeptId);

public sealed record AlertView(
    long Id,
    long StationId,
    long? DeptId,
    AlertType Type,
    AlertLevel Level,
    AlertStatus Status,
    string Source,
    string Message,
    DateTime OccurredAt,
    DateTime ReceivedAt);

public sealed record StationView(
    long StationId,
    string StationCode,
    string OsVersion,
    string CpuArch,
    string SoftwareVersion,
    StationOperationalStatus OperationalStatus,
    LicenseStatus LicenseStatus,
    DateTime? LicenseExpiresAt,
    int LicenseDaysLeft,
    long? DeptId,
    DateTime RegisteredAt);

public sealed record DeptView(long Id, string Code, string Name, long? ParentId, int SortOrder);

public sealed record StorageUsageView(string Location, long FileCount, long TotalSize);

public sealed record StationDetailView(
    long StationId,
    string StationCode,
    string OsVersion,
    string CpuArch,
    string SoftwareVersion,
    StationOperationalStatus OperationalStatus,
    LicenseStatus LicenseStatus,
    DateTime? LicenseExpiresAt,
    int LicenseDaysLeft,
    long? DeptId,
    DateTime RegisteredAt,
    string? DeptName,
    string CpuSerial,
    string MotherboardSerial,
    string DiskSerial,
    string MacAddress,
    int UsbPortCount,
    long ConfigVersion,
    string? StationBaseUrl,
    DateTime? LastHeartbeatAt,
    bool IsOnline,
    long FileCount,
    long TotalSize,
    long TodayFileCount,
    long TodaySize,
    long PendingAlertCount,
    IReadOnlyList<CountItemView> AlertLevels,
    int RecorderCount,
    int WhitelistedRecorderCount,
    IReadOnlyList<StorageUsageView> StorageUsage);
