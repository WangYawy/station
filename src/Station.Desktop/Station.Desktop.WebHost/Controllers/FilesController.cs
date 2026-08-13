using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Station.Application.Authorization;
using Station.Application.Authentication;
using Station.Contracts;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Repositories;
using AuthService = Station.Application.Authorization.IAuthorizationService;

namespace Station.Desktop.WebHost.Controllers;

/// <summary>单机 Web 文件查询（设备/日期/部门/类型/状态筛选 + 元数据 + CSV 导出，按数据范围过滤）。</summary>
[ApiController]
[Authorize]
[Route("api/v1/files")]
public class FilesController : ControllerBase
{
    private readonly IRepository<CollectFile> _files;
    private readonly IRepository<CollectTask> _tasks;
    private readonly IRepository<Dept> _depts;
    private readonly AuthService _authorization;
    private readonly IDataScopeProvider _dataScope;

    public FilesController(
        IRepository<CollectFile> files,
        IRepository<CollectTask> tasks,
        IRepository<Dept> depts,
        AuthService authorization,
        IDataScopeProvider dataScope)
    {
        _files = files;
        _tasks = tasks;
        _depts = depts;
        _authorization = authorization;
        _dataScope = dataScope;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? keyword,
        [FromQuery] string? extension,
        [FromQuery] CollectFileStatus? status,
        [FromQuery] UploadStatus? syncStatus,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20)
    {
        if (!await RequirePermissionAsync(PermissionCodes.FileView))
        {
            return StatusCode(403, new { message = "无文件查看权限" });
        }

        var taskIds = await AllowedTaskIdsAsync();
        var query = _files.AsQueryable().Where(f =>
            taskIds.Contains(f.TaskId) &&
            (extension == null || f.Extension == extension) &&
            (status == null || f.Status == status) &&
            (syncStatus == null || f.SyncStatus == syncStatus) &&
            (from == null || (f.CollectedAt != null && f.CollectedAt >= from)) &&
            (to == null || (f.CollectedAt != null && f.CollectedAt <= to)) &&
            (string.IsNullOrWhiteSpace(keyword) ||
             f.FileName.Contains(keyword) || (f.FileNo != null && f.FileNo.Contains(keyword))));
        var total = query.Count();
        var items = query.OrderBy(f => f.Id, SqlSugar.OrderByType.Desc)
            .ToPageList(Math.Max(1, page), Math.Max(1, size));
        var views = await ToViewsAsync(items);
        return Ok(new { success = true, code = 0, message = "ok", data = new { pageIndex = page, pageSize = size, totalCount = total, items = views } });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string? keyword,
        [FromQuery] string? extension,
        [FromQuery] CollectFileStatus? status,
        [FromQuery] UploadStatus? syncStatus,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string format = "csv")
    {
        if (!await RequirePermissionAsync(PermissionCodes.FileView))
        {
            return StatusCode(403, new { message = "无导出权限" });
        }

        var taskIds = await AllowedTaskIdsAsync();
        var rows = await _files.AsQueryable().Where(f =>
                taskIds.Contains(f.TaskId) &&
                (extension == null || f.Extension == extension) &&
                (status == null || f.Status == status) &&
                (syncStatus == null || f.SyncStatus == syncStatus) &&
                (from == null || (f.CollectedAt != null && f.CollectedAt >= from)) &&
                (to == null || (f.CollectedAt != null && f.CollectedAt <= to)) &&
                (string.IsNullOrWhiteSpace(keyword) ||
                 f.FileName.Contains(keyword) || (f.FileNo != null && f.FileNo.Contains(keyword))))
            .OrderBy(f => f.Id, SqlSugar.OrderByType.Desc)
            .Take(10000)
            .ToListAsync();
        var taskMap = (await _tasks.GetListAsync(t => taskIds.Contains(t.Id)))
            .ToDictionary(t => t.Id);
        return ExportFile("文件台账", format,
            ["编号", "文件名", "类型", "大小(B)", "采集时间", "原始时间", "SM3", "采集状态", "上传状态", "记录仪", "存储位置"],
            rows.Select(f =>
            {
                taskMap.TryGetValue(f.TaskId, out var task);
                return new[]
                {
                    f.FileNo ?? string.Empty, f.FileName, f.Extension, f.Size.ToString(),
                    f.CollectedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
                    f.OriginalModifiedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty,
                    f.Sm3 ?? string.Empty, f.Status.ToString(), f.SyncStatus.ToString(),
                    task?.RecorderSerial ?? task?.RecorderName ?? string.Empty,
                    f.RemotePath ?? string.Empty
                };
            }));
    }

    private FileContentResult ExportFile(string name, string format, string[] headers, IEnumerable<string[]> rows)
    {
        var normalized = format.ToLowerInvariant() is "xlsx" or "pdf" ? format.ToLowerInvariant() : "csv";
        var bytes = Station.Application.Exporting.ExportDocumentBuilder.Build(normalized, name, headers, rows);
        var contentType = normalized switch
        {
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "pdf" => "application/pdf",
            _ => "text/csv; charset=utf-8"
        };
        return File(bytes, contentType, $"{name}_{DateTime.Now:yyyyMMdd_HHmmss}.{normalized}");
    }

    private async Task<List<FileView>> ToViewsAsync(List<CollectFile> files)
    {
        var taskIds = files.Select(f => f.TaskId).Distinct().ToList();
        var tasks = await _tasks.GetListAsync(t => taskIds.Contains(t.Id));
        var taskMap = tasks.ToDictionary(t => t.Id);
        var deptIds = tasks.Where(t => t.DeptId != null).Select(t => t.DeptId!.Value).Distinct().ToList();
        var deptMap = (await _depts.GetListAsync(d => deptIds.Contains(d.Id))).ToDictionary(d => d.Id, d => d.Name);
        return files.Select(f =>
        {
            taskMap.TryGetValue(f.TaskId, out var task);
            return new FileView(
                f.Id, f.FileNo, f.FileName, f.Extension, f.Size,
                f.Status, f.SyncStatus, f.CollectedAt, f.OriginalModifiedAt, f.Sm3,
                task?.RecorderSerial ?? task?.RecorderName ?? string.Empty,
                task?.OperatorUserId,
                task?.DeptId is { } did && deptMap.TryGetValue(did, out var name) ? name : null,
                f.RemotePath, f.ErrorMessage);
        }).ToList();
    }

    private async Task<List<long>> AllowedTaskIdsAsync()
    {
        var session = await CurrentSessionAsync();
        var scope = await WebScopeHelper.GetScopeAsync(User, _authorization, _dataScope);
        var query = _tasks.AsQueryable();
        if (scope.IsAll)
        {
            return (await query.Select(t => t.Id).ToListAsync()).Distinct().ToList();
        }

        if (scope.Scope == DataScope.Self)
        {
            var selfUserId = session?.UserId;
            query = query.Where(t => t.OperatorUserId == selfUserId);
        }
        else
        {
            query = query.Where(t => t.DeptId != null && scope.AllowedDeptIds.Contains(t.DeptId.Value));
        }

        return (await query.Select(t => t.Id).ToListAsync()).Distinct().ToList();
    }

    private async Task<AuthSession?> CurrentSessionAsync()
    {
        if (User.FindFirst("accountId") is not { } accountClaim ||
            !long.TryParse(accountClaim.Value, out var accountId))
        {
            return null;
        }

        return await _authorization.GetSessionAsync(accountId);
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

public sealed record FileView(
    long Id,
    string? FileNo,
    string FileName,
    string Extension,
    long Size,
    CollectFileStatus Status,
    UploadStatus SyncStatus,
    DateTime? CollectedAt,
    DateTime? OriginalModifiedAt,
    string? Sm3,
    string Recorder,
    long? OperatorUserId,
    string? DeptName,
    string? RemotePath,
    string? ErrorMessage);
