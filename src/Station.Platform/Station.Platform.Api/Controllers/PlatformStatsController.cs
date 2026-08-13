using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using Station.Application.Authorization;
using Station.Contracts;
using Station.Contracts.Api;
using Station.Domain.Entities;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Repositories;
using Station.Platform.Domain.Entities;
using AuthService = Station.Application.Authorization.IAuthorizationService;

namespace Station.Platform.Api.Controllers;

/// <summary>跨站汇总统计（概览/采集趋势/采集排行/报警统计），全部按部门树数据权限过滤。</summary>
[ApiController]
[Authorize]
[Route("api/v1/stats")]
public class PlatformStatsController : ControllerBase
{
    private readonly IRepository<PlatformStation> _stations;
    private readonly IRepository<PlatformFileMetadata> _files;
    private readonly IRepository<PlatformAlertReport> _alerts;
    private readonly IRepository<Dept> _depts;
    private readonly AuthService _authorization;
    private readonly IDataScopeProvider _dataScope;
    private readonly int _onlineTimeoutSeconds;

    public PlatformStatsController(
        IRepository<PlatformStation> stations,
        IRepository<PlatformFileMetadata> files,
        IRepository<PlatformAlertReport> alerts,
        IRepository<Dept> depts,
        AuthService authorization,
        IDataScopeProvider dataScope,
        IConfiguration configuration)
    {
        _stations = stations;
        _files = files;
        _alerts = alerts;
        _depts = depts;
        _authorization = authorization;
        _dataScope = dataScope;
        _onlineTimeoutSeconds = configuration.GetValue("Platform:OnlineTimeoutSeconds", 300);
    }

    /// <summary>概览：采集站（含在线/离线）、文件/容量、今日采集、待处理报警。</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview()
    {
        if (!await RequirePermissionAsync(PermissionCodes.FileView))
        {
            return StatusCode(403, new { message = "无统计查看权限" });
        }

        var scope = await GetScopeAsync();
        var onlineCutoff = DateTime.Now.AddSeconds(-_onlineTimeoutSeconds);
        var stationCount = await ApplyStationScope(_stations.AsQueryable(), scope).CountAsync();
        var onlineCount = await ApplyStationScope(_stations.AsQueryable(), scope)
            .Where(s => s.LastHeartbeatAt != null && s.LastHeartbeatAt >= onlineCutoff)
            .CountAsync();
        var fileCount = await ApplyFileScope(_files.AsQueryable(), scope).CountAsync();
        var totalSize = await ApplyFileScope(_files.AsQueryable(), scope).SumAsync(f => f.Size);

        var todayStart = DateTime.Today;
        var todayFileCount = await ApplyFileScope(_files.AsQueryable(), scope)
            .Where(f => f.CollectedAt >= todayStart)
            .CountAsync();
        var todaySize = await ApplyFileScope(_files.AsQueryable(), scope)
            .Where(f => f.CollectedAt >= todayStart)
            .SumAsync(f => f.Size);
        var videoCount = await ApplyFileScope(_files.AsQueryable(), scope)
            .Where(f => f.Kind == FileKind.Video)
            .CountAsync();

        var pendingAlertCount = await ApplyAlertScope(_alerts.AsQueryable(), scope)
            .Where(a => a.Status == AlertStatus.Pending)
            .CountAsync();
        var alertCount = await ApplyAlertScope(_alerts.AsQueryable(), scope).CountAsync();

        return Ok(ApiResponse<OverviewStatsView>.Ok(new OverviewStatsView(
            stationCount, onlineCount, Math.Max(0, stationCount - onlineCount),
            fileCount, totalSize, todayFileCount, todaySize, videoCount,
            pendingAlertCount, alertCount)));
    }

    /// <summary>采集趋势：近 N 天（默认 14，上限 365）每日文件数与容量。</summary>
    [HttpGet("collection-trend")]
    public async Task<IActionResult> CollectionTrend(
        [FromQuery] int days = 14,
        [FromQuery] long? stationId = null,
        [FromQuery] long? deptId = null)
    {
        if (!await RequirePermissionAsync(PermissionCodes.FileView))
        {
            return StatusCode(403, new { message = "无统计查看权限" });
        }

        var scope = await GetScopeAsync();
        days = Math.Clamp(days, 1, 365);
        var start = DateTime.Today.AddDays(-(days - 1));
        var query = ApplyFileScope(_files.AsQueryable(), scope)
            .Where(f => f.CollectedAt >= start);
        if (stationId is { } sid)
        {
            query = query.Where(f => f.StationId == sid);
        }

        if (deptId is { } did)
        {
            query = query.Where(f => f.DeptId == did);
        }

        var rows = await query.Select(f => new { f.CollectedAt, f.Size }).ToListAsync();
        var byDate = rows
            .GroupBy(f => f.CollectedAt.Date)
            .ToDictionary(g => g.Key, g => (Count: g.LongCount(), Size: g.Sum(x => x.Size)));
        var points = Enumerable.Range(0, days).Select(i =>
        {
            var date = start.AddDays(i);
            byDate.TryGetValue(date, out var agg);
            return new TrendPointView(date, agg.Count, agg.Size);
        }).ToList();
        return Ok(ApiResponse<List<TrendPointView>>.Ok(points));
    }

    /// <summary>采集排行：各采集站文件数/容量/报警数/最后采集/在线状态。</summary>
    [HttpGet("stations")]
    public async Task<IActionResult> Stations([FromQuery] int page = 1, [FromQuery] int size = 50)
    {
        if (!await RequirePermissionAsync(PermissionCodes.FileView))
        {
            return StatusCode(403, new { message = "无统计查看权限" });
        }

        var scope = await GetScopeAsync();
        var stationQuery = ApplyStationScope(_stations.AsQueryable(), scope);
        var stations = await stationQuery.ToListAsync();

        var visibleIds = stations.Select(s => s.Id).ToHashSet();
        var deptIds = stations.Where(s => s.DeptId != null).Select(s => s.DeptId!.Value).Distinct().ToList();
        var deptMap = (await _depts.GetListAsync(d => deptIds.Contains(d.Id)))
            .ToDictionary(d => d.Id, d => d.Name);

        var files = await ApplyFileScope(_files.AsQueryable(), scope)
            .Where(f => visibleIds.Contains(f.StationId))
            .Select(f => new { f.StationId, f.Size, f.CollectedAt })
            .ToListAsync();
        var alerts = await ApplyAlertScope(_alerts.AsQueryable(), scope)
            .Where(a => visibleIds.Contains(a.StationId))
            .Select(a => new { a.StationId })
            .ToListAsync();

        var fileAgg = files
            .GroupBy(f => f.StationId)
            .ToDictionary(g => g.Key, g => (Count: g.LongCount(), Size: g.Sum(x => x.Size), Last: g.Max(x => x.CollectedAt)));
        var alertCounts = alerts.GroupBy(a => a.StationId)
            .ToDictionary(g => g.Key, g => g.LongCount());
        var onlineCutoff = DateTime.Now.AddSeconds(-_onlineTimeoutSeconds);

        var items = stations.Select(s =>
        {
            fileAgg.TryGetValue(s.Id, out var f);
            alertCounts.TryGetValue(s.Id, out var alertCount);
            return new StationStatView(
                s.Id,
                s.StationCode,
                s.DeptId,
                s.DeptId is { } did && deptMap.TryGetValue(did, out var name) ? name : null,
                f.Count,
                f.Size,
                alertCount,
                f.Last,
                s.LastHeartbeatAt != null && s.LastHeartbeatAt >= onlineCutoff);
        })
            .OrderByDescending(x => x.FileCount)
            .ThenBy(x => x.StationId)
            .ToList();
        var total = items.Count;
        var paged = items
            .Skip((Math.Max(1, page) - 1) * Math.Max(1, size))
            .Take(Math.Max(1, size))
            .ToList();

        return Ok(ApiResponse<PagedResult<StationStatView>>.Ok(
            new PagedResult<StationStatView>(page, size, total, paged)));
    }

    /// <summary>报警统计：按级别/处置状态/类型汇总。</summary>
    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts()
    {
        if (!await RequirePermissionAsync(PermissionCodes.FileView))
        {
            return StatusCode(403, new { message = "无统计查看权限" });
        }

        var scope = await GetScopeAsync();
        var rows = await ApplyAlertScope(_alerts.AsQueryable(), scope)
            .Select(a => new { a.Level, a.Status, a.Type })
            .ToListAsync();
        var byLevel = rows.GroupBy(a => a.Level)
            .Select(g => new CountItemView(((int)g.Key).ToString(), g.LongCount()))
            .OrderBy(x => int.Parse(x.Key))
            .ToList();
        var byStatus = rows.GroupBy(a => a.Status)
            .Select(g => new CountItemView(((int)g.Key).ToString(), g.LongCount()))
            .OrderBy(x => int.Parse(x.Key))
            .ToList();
        var byType = rows.GroupBy(a => a.Type)
            .Select(g => new CountItemView(((int)g.Key).ToString(), g.LongCount()))
            .OrderBy(x => int.Parse(x.Key))
            .ToList();
        return Ok(ApiResponse<AlertStatsView>.Ok(new AlertStatsView(byLevel, byStatus, byType)));
    }

    private async Task<bool> RequirePermissionAsync(string code)
    {
        if (User.FindFirst("accountId") is not { } accountClaim ||
            !long.TryParse(accountClaim.Value, out var accountId))
        {
            return false;
        }

        return await _authorization.HasPermissionAsync(accountId, code);
    }

    private Task<DataScopeResult> GetScopeAsync() =>
        DataScopeHelper.GetScopeAsync(User, _authorization, _dataScope);

    private static ISugarQueryable<PlatformStation> ApplyStationScope(
        ISugarQueryable<PlatformStation> query, DataScopeResult scope)
    {
        if (!scope.IsAll)
        {
            query = query.Where(s => s.DeptId != null && scope.AllowedDeptIds.Contains(s.DeptId.Value));
        }

        return query;
    }

    private static ISugarQueryable<PlatformFileMetadata> ApplyFileScope(
        ISugarQueryable<PlatformFileMetadata> query, DataScopeResult scope)
    {
        if (!scope.IsAll)
        {
            query = query.Where(f => f.DeptId != null && scope.AllowedDeptIds.Contains(f.DeptId.Value));
        }

        return query;
    }

    private static ISugarQueryable<PlatformAlertReport> ApplyAlertScope(
        ISugarQueryable<PlatformAlertReport> query, DataScopeResult scope)
    {
        if (!scope.IsAll)
        {
            query = query.Where(a => a.DeptId != null && scope.AllowedDeptIds.Contains(a.DeptId.Value));
        }

        return query;
    }
}

public sealed record OverviewStatsView(
    long StationCount,
    long OnlineCount,
    long OfflineCount,
    long FileCount,
    long TotalSize,
    long TodayFileCount,
    long TodaySize,
    long VideoCount,
    long PendingAlertCount,
    long AlertCount);

public sealed record TrendPointView(DateTime Date, long FileCount, long Size);

public sealed record StationStatView(
    long StationId,
    string StationCode,
    long? DeptId,
    string? DeptName,
    long FileCount,
    long TotalSize,
    long AlertCount,
    DateTime? LastCollectedAt,
    bool IsOnline);

public sealed record CountItemView(string Key, long Count);

public sealed record AlertStatsView(
    List<CountItemView> ByLevel,
    List<CountItemView> ByStatus,
    List<CountItemView> ByType);
