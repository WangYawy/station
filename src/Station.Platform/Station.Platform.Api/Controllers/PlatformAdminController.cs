using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SqlSugar;
using Station.Contracts;
using Station.Contracts.Api;
using AppAuthorization = Station.Application.Authorization.IAuthorizationService;
using Station.Application.Authorization;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure.Repositories;
using Station.Platform.Domain.Entities;
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
    private readonly IRepository<Dept> _depts;
    private readonly AppAuthorization _authorization;
    private readonly IDataScopeProvider _dataScope;

    public PlatformAdminController(
        IRepository<PlatformAlertReport> alerts,
        IRepository<PlatformStation> stations,
        IRepository<Dept> depts,
        AppAuthorization authorization,
        IDataScopeProvider dataScope)
    {
        _alerts = alerts;
        _stations = stations;
        _depts = depts;
        _authorization = authorization;
        _dataScope = dataScope;
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
                s.LicenseStatus, s.LicenseExpiresAt, s.LicenseDaysLeft, s.DeptId, s.RegisteredAt)).ToList())));
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

    private async Task<DataScopeResult> GetScopeAsync()
    {
        return await DataScopeHelper.GetScopeAsync(User, _authorization, _dataScope);
    }
}

    public sealed record SetAlertStatusRequest(AlertStatus Status);

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
    LicenseStatus LicenseStatus,
    DateTime? LicenseExpiresAt,
    int LicenseDaysLeft,
    long? DeptId,
    DateTime RegisteredAt);

public sealed record DeptView(long Id, string Code, string Name, long? ParentId, int SortOrder);
