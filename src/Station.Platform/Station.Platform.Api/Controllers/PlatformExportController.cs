using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using Station.Application.Exporting;
using Station.Application.Authorization;
using Station.Contracts;
using Station.Contracts.Alerts;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Repositories;
using Station.Platform.Domain.Entities;
using AuthService = Station.Application.Authorization.IAuthorizationService;

namespace Station.Platform.Api.Controllers;

/// <summary>
/// 台账/报表 CSV 导出：与列表同一套权限码 + 部门树数据范围；审计导出对来源 IP 脱敏。
/// CSV 使用 UTF-8 BOM，Excel 可直接打开。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/exports")]
public class PlatformExportController : ControllerBase
{
    private readonly IRepository<PlatformFileMetadata> _files;
    private readonly IRepository<PlatformAlertReport> _alerts;
    private readonly IRepository<PlatformStation> _stations;
    private readonly IRepository<PlatformRecorder> _recorders;
    private readonly IRepository<AuditLog> _auditLogs;
    private readonly IRepository<Dept> _depts;
    private readonly AuthService _authorization;
    private readonly IDataScopeProvider _dataScope;

    public PlatformExportController(
        IRepository<PlatformFileMetadata> files,
        IRepository<PlatformAlertReport> alerts,
        IRepository<PlatformStation> stations,
        IRepository<PlatformRecorder> recorders,
        IRepository<AuditLog> auditLogs,
        IRepository<Dept> depts,
        AuthService authorization,
        IDataScopeProvider dataScope)
    {
        _files = files;
        _alerts = alerts;
        _stations = stations;
        _recorders = recorders;
        _auditLogs = auditLogs;
        _depts = depts;
        _authorization = authorization;
        _dataScope = dataScope;
    }

    [HttpGet("files")]
    public async Task<IActionResult> ExportFiles(
        [FromQuery] long? stationId,
        [FromQuery] long? deptId,
        [FromQuery] string? keyword,
        [FromQuery] int? kind,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string format = "csv")
    {
        if (!await RequirePermissionAsync(PermissionCodes.FileView))
        {
            return StatusCode(403, new { message = "无导出权限" });
        }

        var scope = await GetScopeAsync();
        var query = _files.AsQueryable().Where(f =>
            (stationId == null || f.StationId == stationId) &&
            (deptId == null || f.DeptId == deptId) &&
            (kind == null || f.Kind == (FileKind)kind) &&
            (from == null || f.CollectedAt >= from) &&
            (to == null || f.CollectedAt <= to) &&
            (string.IsNullOrWhiteSpace(keyword) ||
             f.FileName.Contains(keyword) || f.FileNo.Contains(keyword) || f.RecorderSerial.Contains(keyword)));
        if (!scope.IsAll)
        {
            query = query.Where(f => f.DeptId != null && scope.AllowedDeptIds.Contains(f.DeptId.Value));
        }

        var rows = await query.OrderBy(f => f.CollectedAt, SqlSugar.OrderByType.Desc)
            .Take(10000)
            .ToListAsync();
        return ExportFile("文件台账", format,
            ["编号", "文件名", "类型", "大小(B)", "采集时间", "记录仪", "用户", "部门", "存储位置"],
            rows.Select(f => new[]
            {
                f.FileNo, f.FileName, FileKindNames[(int)f.Kind], f.Size.ToString(),
                f.CollectedAt.ToString("yyyy-MM-dd HH:mm:ss"), f.RecorderSerial,
                f.UserNo ?? string.Empty, f.DeptCode ?? string.Empty, f.StorageLocation ?? string.Empty
            }));
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> ExportAlerts(
        [FromQuery] long? stationId,
        [FromQuery] int? level,
        [FromQuery] int? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string format = "csv")
    {
        if (!await RequirePermissionAsync(PermissionCodes.AlertView))
        {
            return StatusCode(403, new { message = "无导出权限" });
        }

        var scope = await GetScopeAsync();
        var query = _alerts.AsQueryable().Where(a =>
            (stationId == null || a.StationId == stationId) &&
            (level == null || a.Level == (AlertLevel)level) &&
            (status == null || a.Status == (AlertStatus)status) &&
            (from == null || a.OccurredAt >= from) &&
            (to == null || a.OccurredAt <= to));
        if (!scope.IsAll)
        {
            query = query.Where(a => a.DeptId != null && scope.AllowedDeptIds.Contains(a.DeptId.Value));
        }

        var rows = await query.OrderBy(a => a.OccurredAt, SqlSugar.OrderByType.Desc)
            .Take(10000)
            .ToListAsync();
        return ExportFile("报警台账", format,
            ["ID", "采集站", "级别", "类型", "来源", "内容", "状态", "发生时间", "接收时间"],
            rows.Select(a => new[]
            {
                a.Id.ToString(), a.StationId.ToString(), AlertLevelNames[(int)a.Level], AlertTypeNames[(int)a.Type],
                a.Source, a.Message, AlertStatusNames[(int)a.Status],
                a.OccurredAt.ToString("yyyy-MM-dd HH:mm:ss"), a.ReceivedAt.ToString("yyyy-MM-dd HH:mm:ss")
            }));
    }

    [HttpGet("stations")]
    public async Task<IActionResult> ExportStations([FromQuery] string format = "csv")
    {
        if (!await RequirePermissionAsync(PermissionCodes.StationView))
        {
            return StatusCode(403, new { message = "无导出权限" });
        }

        var scope = await GetScopeAsync();
        var query = _stations.AsQueryable();
        if (!scope.IsAll)
        {
            query = query.Where(s => s.DeptId != null && scope.AllowedDeptIds.Contains(s.DeptId.Value));
        }

        var rows = await query.OrderBy(s => s.Id, SqlSugar.OrderByType.Asc).ToListAsync();
        return ExportFile("采集站台账", format,
            ["站ID", "站编号", "系统", "架构", "版本", "授权状态", "到期时间", "剩余天数", "部门ID", "注册时间"],
            rows.Select(s => new[]
            {
                s.Id.ToString(), s.StationCode, s.OsVersion, s.CpuArch, s.SoftwareVersion,
                LicenseStatusNames[(int)s.LicenseStatus],
                s.LicenseExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
                s.LicenseDaysLeft.ToString(), s.DeptId?.ToString() ?? string.Empty,
                s.RegisteredAt.ToString("yyyy-MM-dd HH:mm:ss")
            }));
    }

    [HttpGet("recorders")]
    public async Task<IActionResult> ExportRecorders(
        [FromQuery] string? keyword,
        [FromQuery] bool? whitelisted,
        [FromQuery] bool? bound,
        [FromQuery] string format = "csv")
    {
        if (!await RequirePermissionAsync(PermissionCodes.RecorderView))
        {
            return StatusCode(403, new { message = "无导出权限" });
        }

        var scope = await GetScopeAsync();
        var query = _recorders.AsQueryable().Where(r =>
            string.IsNullOrWhiteSpace(keyword) ||
            r.RecorderSerial.Contains(keyword) ||
            (r.BoundUserNo != null && r.BoundUserNo.Contains(keyword)) ||
            (r.BoundUserName != null && r.BoundUserName.Contains(keyword)) ||
            (r.BoundDeptCode != null && r.BoundDeptCode.Contains(keyword)));
        if (whitelisted is { } wl)
        {
            query = query.Where(r => r.IsWhitelisted == wl);
        }

        if (bound == true)
        {
            query = query.Where(r => r.BoundUserNo != null);
        }

        if (!scope.IsAll)
        {
            query = query.Where(r => r.DeptId != null && scope.AllowedDeptIds.Contains(r.DeptId.Value));
        }

        var rows = await query.OrderBy(r => r.LastSeenAt, SqlSugar.OrderByType.Desc).Take(10000).ToListAsync();
        return ExportFile("记录仪台账", format,
            ["记录仪编号", "最近采集站", "文件数", "容量(B)", "最近采集时间", "首次上报", "末次上报", "绑定用户", "绑定部门", "白名单"],
            rows.Select(r => new[]
            {
                r.RecorderSerial, r.LastStationId?.ToString() ?? string.Empty, r.FileCount.ToString(),
                r.TotalSize.ToString(), r.LastFileAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
                r.FirstSeenAt.ToString("yyyy-MM-dd HH:mm:ss"), r.LastSeenAt.ToString("yyyy-MM-dd HH:mm:ss"),
                r.BoundUserName ?? r.BoundUserNo ?? string.Empty,
                r.BoundDeptName ?? r.BoundDeptCode ?? string.Empty,
                r.IsWhitelisted ? "是" : "否"
            }));
    }

    [HttpGet("audit-logs")]
    public async Task<IActionResult> ExportAuditLogs(
        [FromQuery] string? keyword,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string format = "csv")
    {
        if (!await RequirePermissionAsync(PermissionCodes.AuditExport))
        {
            return StatusCode(403, new { message = "无导出权限" });
        }

        var scope = await GetScopeAsync();
        var query = _auditLogs.AsQueryable().Where(a =>
            (from == null || a.CreatedAt >= from) &&
            (to == null || a.CreatedAt <= to) &&
            (string.IsNullOrWhiteSpace(keyword) ||
             (a.OperatorAccount != null && a.OperatorAccount.Contains(keyword)) ||
             (a.OperatorName != null && a.OperatorName.Contains(keyword)) ||
             a.OperationType.Contains(keyword) ||
             (a.Target != null && a.Target.Contains(keyword)) ||
             (a.Detail != null && a.Detail.Contains(keyword))));
        if (!scope.IsAll)
        {
            query = query.Where(a => a.DeptId != null && scope.AllowedDeptIds.Contains(a.DeptId.Value));
        }

        var rows = await query.OrderBy(a => a.CreatedAt, SqlSugar.OrderByType.Desc)
            .Take(10000)
            .ToListAsync();
        return ExportFile("审计日志", format,
            ["时间", "操作人", "账号", "部门ID", "来源IP(脱敏)", "类型", "目标", "详情", "结果"],
            rows.Select(a => new[]
            {
                a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                a.OperatorName ?? string.Empty, a.OperatorAccount ?? string.Empty,
                a.DeptId?.ToString() ?? string.Empty, MaskIp(a.SourceIp),
                a.OperationType, a.Target ?? string.Empty, a.Detail ?? string.Empty,
                a.Result == 1 ? "成功" : "失败"
            }));
    }

    [HttpGet("stats-trend")]
    public async Task<IActionResult> ExportStatsTrend([FromQuery] int days = 30, [FromQuery] string format = "csv")
    {
        if (!await RequirePermissionAsync(PermissionCodes.FileView))
        {
            return StatusCode(403, new { message = "无导出权限" });
        }

        var scope = await GetScopeAsync();
        days = Math.Clamp(days, 1, 365);
        var start = DateTime.Today.AddDays(-(days - 1));
        var query = _files.AsQueryable().Where(f => f.CollectedAt >= start);
        if (!scope.IsAll)
        {
            query = query.Where(f => f.DeptId != null && scope.AllowedDeptIds.Contains(f.DeptId.Value));
        }

        var rows = await query.Select(f => new { f.CollectedAt, f.Size }).ToListAsync();
        var byDate = rows.GroupBy(f => f.CollectedAt.Date)
            .ToDictionary(g => g.Key, g => (Count: g.LongCount(), Size: g.Sum(x => x.Size)));
        var points = Enumerable.Range(0, days).Select(i =>
        {
            var date = start.AddDays(i);
            byDate.TryGetValue(date, out var agg);
            return new[] { date.ToString("yyyy-MM-dd"), agg.Count.ToString(), agg.Size.ToString() };
        });
        return ExportFile("采集趋势", format, ["日期", "文件数", "容量(B)"], points);
    }

    [HttpGet("stats-stations")]
    public async Task<IActionResult> ExportStatsStations([FromQuery] string format = "csv")
    {
        if (!await RequirePermissionAsync(PermissionCodes.FileView))
        {
            return StatusCode(403, new { message = "无导出权限" });
        }

        var scope = await GetScopeAsync();
        var stationQuery = _stations.AsQueryable();
        if (!scope.IsAll)
        {
            stationQuery = stationQuery.Where(s => s.DeptId != null && scope.AllowedDeptIds.Contains(s.DeptId.Value));
        }

        var stations = await stationQuery.ToListAsync();
        var ids = stations.Select(s => s.Id).ToHashSet();
        var deptIds = stations.Where(s => s.DeptId != null).Select(s => s.DeptId!.Value).Distinct().ToList();
        var deptMap = (await _depts.GetListAsync(d => deptIds.Contains(d.Id))).ToDictionary(d => d.Id, d => d.Name);
        var fileRows = await _files.AsQueryable()
            .Where(f => ids.Contains(f.StationId))
            .Select(f => new { f.StationId, f.Size })
            .ToListAsync();
        var fileAgg = fileRows.GroupBy(f => f.StationId)
            .ToDictionary(g => g.Key, g => (Count: g.LongCount(), Size: g.Sum(x => x.Size)));
        var alertRows = await _alerts.AsQueryable()
            .Where(a => ids.Contains(a.StationId))
            .Select(a => a.StationId)
            .ToListAsync();
        var alertCounts = alertRows.GroupBy(a => a).ToDictionary(g => g.Key, g => g.LongCount());
        return ExportFile("采集排行", format,
            ["站", "部门", "文件数", "容量(B)", "报警数", "在线"],
            stations.OrderByDescending(s => fileAgg.GetValueOrDefault(s.Id).Count)
                .Select(s =>
                {
                    fileAgg.TryGetValue(s.Id, out var fa);
                    alertCounts.TryGetValue(s.Id, out var ac);
                    var online = s.LastHeartbeatAt != null &&
                                 s.LastHeartbeatAt >= DateTime.Now.AddSeconds(-300);
                    return new[]
                    {
                        s.StationCode,
                        s.DeptId is { } did && deptMap.TryGetValue(did, out var name) ? name : string.Empty,
                        fa.Count.ToString(), fa.Size.ToString(), ac.ToString(), online ? "在线" : "离线"
                    };
                }));
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

    private FileContentResult ExportFile(string name, string format, string[] headers, IEnumerable<string[]> rows)
    {
        var normalized = format.ToLowerInvariant() is "xlsx" or "pdf" ? format.ToLowerInvariant() : "csv";
        var bytes = ExportDocumentBuilder.Build(normalized, name, headers, rows);
        var contentType = normalized switch
        {
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "pdf" => "application/pdf",
            _ => "text/csv; charset=utf-8"
        };
        return File(bytes, contentType, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.{normalized}");
    }

    private static string MaskIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return string.Empty;
        }

        var parts = ip.Split('.');
        return parts.Length == 4
            ? $"{parts[0]}.{parts[1]}.{parts[2]}.*"
            : ip;
    }

    private static readonly string[] FileKindNames = ["视频", "音频", "图片", "其他"];
    private static readonly string[] AlertLevelNames = ["提示", "警告", "严重"];
    private static readonly string[] AlertStatusNames = ["待处理", "已确认", "已处理", "已关闭"];
    private static readonly string[] AlertTypeNames =
    [
        "磁盘不足", "网络中断", "USB故障", "校验失败", "非授权接入", "绑定异常", "存储不可达", "授权到期"
    ];
    private static readonly string[] LicenseStatusNames = ["试用", "宽限", "已激活", "锁定"];
}
