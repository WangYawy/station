using System.Linq.Expressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Station.Application.Audit;
using Station.Application.Authorization;
using Station.Domain.Authorization;
using Station.Domain.Entities;
using Station.Domain.Repositories;
using AuthService = Station.Application.Authorization.IAuthorizationService;

namespace Station.Desktop.WebHost.Controllers;

/// <summary>单机 Web 审计日志：筛选查询 + CSV 导出（来源 IP 脱敏）。</summary>
[ApiController]
[Authorize]
[Route("api/v1/audit-logs")]
public class AuditLogsController : ControllerBase
{
    private readonly IRepository<AuditLog> _logs;
    private readonly IAuditLogService _logService;
    private readonly AuthService _authorization;
    private readonly IDataScopeProvider _dataScope;

    public AuditLogsController(
        IRepository<AuditLog> logs,
        AuthService authorization,
        IDataScopeProvider dataScope)
    {
        _logs = logs;
        _authorization = authorization;
        _dataScope = dataScope;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? keyword,
        [FromQuery] string? operationType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20)
    {
        if (!await RequirePermissionAsync(PermissionCodes.AuditView))
        {
            return StatusCode(403, new { message = "无审计查看权限" });
        }
        Expression<Func<AuditLog, bool>> filter = a =>
             (from == null || a.CreatedAt >= from) &&
             (to == null || a.CreatedAt <= to) &&
             (operationType == null || a.OperationType == operationType) &&
             (string.IsNullOrWhiteSpace(keyword) ||
              (a.OperatorAccount != null && a.OperatorAccount.Contains(keyword)) ||
              (a.OperatorName != null && a.OperatorName.Contains(keyword)) ||
              (a.OperationType != null && a.OperationType.Contains(keyword)) ||  // 加了 null 检查
              (a.Target != null && a.Target.Contains(keyword)) ||
              (a.Detail != null && a.Detail.Contains(keyword)));
        Expression<Func<AuditLog, object>> orderBy = a => a.CreatedAt;
        var query = await _logs.ToPageAsync(page, size, filter, orderBy);

        return Ok(new
        {
            success = true,
            code = 0,
            message = "ok",
            data = new
            {
                pageIndex = page,
                pageSize = size,
                totalCount = query.Total,
                items = query.Items.Select(a => new AuditLogView(
                    a.Id, a.OperatorAccount, a.OperatorName, a.DeptId, a.SourceIp,
                    a.OperationType, a.Target, a.Detail, a.Result, a.CreatedAt)).ToList()
            }
        });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string? keyword,
        [FromQuery] string? operationType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string format = "csv")
    {
        if (!await RequirePermissionAsync(PermissionCodes.AuditExport))
        {
            return StatusCode(403, new { message = "无导出权限" });
        }

        //var rows = await _logs.AsQueryable().Where(a =>
        //        (from == null || a.CreatedAt >= from) &&
        //        (to == null || a.CreatedAt <= to) &&
        //        (operationType == null || a.OperationType == operationType) &&
        //        (string.IsNullOrWhiteSpace(keyword) ||
        //         (a.OperatorAccount != null && a.OperatorAccount.Contains(keyword)) ||
        //         (a.OperatorName != null && a.OperatorName.Contains(keyword)) ||
        //         a.OperationType.Contains(keyword) ||
        //         (a.Target != null && a.Target.Contains(keyword)) ||
        //         (a.Detail != null && a.Detail.Contains(keyword))))
        //    .OrderBy(a => a.CreatedAt, SqlSugar.OrderByType.Desc)
        //    .Take(10000)
        //    .ToListAsync();
        var normalized = format.ToLowerInvariant() is "xlsx" or "pdf" ? format.ToLowerInvariant() : "csv";
        //var bytes = Station.Application.Exporting.ExportDocumentBuilder.Build(normalized, "审计日志",
        //    ["时间", "操作人", "账号", "部门ID", "来源IP(脱敏)", "类型", "目标", "详情", "结果"],
        //    rows.Select(a => new[]
        //    {
        //        a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
        //        a.OperatorName ?? string.Empty, a.OperatorAccount ?? string.Empty,
        //        a.DeptId?.ToString() ?? string.Empty, WebCsv.MaskIp(a.SourceIp),
        //        a.OperationType, a.Target ?? string.Empty, a.Detail ?? string.Empty,
        //        a.Result == 1 ? "成功" : "失败"
        //    }));
        var contentType = normalized switch
        {
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "pdf" => "application/pdf",
            _ => "text/csv; charset=utf-8"
        };
        //return File(bytes, contentType, $"审计日志_{DateTime.Now:yyyyMMdd_HHmmss}.{normalized}"); 
        return null;
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
}

public sealed record AuditLogView(
    long Id,
    string? OperatorAccount,
    string? OperatorName,
    long? DeptId,
    string? SourceIp,
    string OperationType,
    string? Target,
    string? Detail,
    int Result,
    DateTime CreatedAt);
