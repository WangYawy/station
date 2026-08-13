using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SqlSugar;
using Station.Contracts;
using Station.Contracts.Api;
using AppAuthorization = Station.Application.Authorization.IAuthorizationService;
using Station.Infrastructure.Repositories;
using Station.Platform.Domain.Entities;

namespace Station.Platform.Api.Controllers;

/// <summary>平台报警/采集站授权管理查询。</summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public class PlatformAdminController : ControllerBase
{
    private readonly IRepository<PlatformAlertReport> _alerts;
    private readonly IRepository<PlatformStation> _stations;
    private readonly AppAuthorization _authorization;

    public PlatformAdminController(
        IRepository<PlatformAlertReport> alerts,
        IRepository<PlatformStation> stations,
        AppAuthorization authorization)
    {
        _alerts = alerts;
        _stations = stations;
        _authorization = authorization;
    }

    [HttpGet("alerts")]
    public async Task<ActionResult<ApiResponse<PagedResult<AlertView>>>> ListAlerts(
        [FromQuery] long? stationId,
        [FromQuery] AlertLevel? level,
        [FromQuery] AlertStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20)
    {
        var query = _alerts.AsQueryable()
            .Where(a =>
                (stationId == null || a.StationId == stationId) &&
                (level == null || a.Level == level) &&
                (status == null || a.Status == status));
        var total = query.Count();
        var items = query.OrderBy(a => a.OccurredAt, OrderByType.Desc)
            .ToPageList(Math.Max(1, page), Math.Max(1, size));
        return Ok(ApiResponse<PagedResult<AlertView>>.Ok(new PagedResult<AlertView>(
            page, size, total, items.Select(a => new AlertView(
                a.Id, a.StationId, a.Type, a.Level, a.Status, a.Source, a.Message, a.OccurredAt, a.ReceivedAt)).ToList())));
    }

    [HttpGet("stations")]
    public async Task<ActionResult<ApiResponse<PagedResult<StationView>>>> ListStations(
        [FromQuery] int page = 1,
        [FromQuery] int size = 50)
    {
        var query = _stations.AsQueryable();
        var total = query.Count();
        var items = query.OrderBy(s => s.Id, OrderByType.Asc)
            .ToPageList(Math.Max(1, page), Math.Max(1, size));
        return Ok(ApiResponse<PagedResult<StationView>>.Ok(new PagedResult<StationView>(
            page, size, total, items.Select(s => new StationView(
                s.Id, s.StationCode, s.OsVersion, s.CpuArch, s.SoftwareVersion,
                s.LicenseStatus, s.LicenseExpiresAt, s.LicenseDaysLeft, s.RegisteredAt)).ToList())));
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
}

public sealed record SetAlertStatusRequest(AlertStatus Status);

public sealed record AlertView(
    long Id,
    long StationId,
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
    DateTime RegisteredAt);
