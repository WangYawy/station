using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Station.Application.Audit;
using Station.Domain.Entities;
using Station.Infrastructure.Backup;

namespace Station.Platform.Api.Controllers;

/// <summary>平台备份：手动备份 + 备份列表（仅管理员），备份动作留审计。</summary>
[ApiController]
[Authorize]
[Route("api/v1/backups")]
public class BackupsController : ControllerBase
{
    private readonly IDatabaseBackupService _backups;
    private readonly IAuditLogService _audit;

    public BackupsController(IDatabaseBackupService backups, IAuditLogService audit)
    {
        _backups = backups;
        _audit = audit;
    }

    [HttpGet]
    public IActionResult List()
    {
        if (!IsAdmin())
        {
            return StatusCode(403, new { message = "仅管理员可查看备份" });
        }

        return Ok(new { success = true, code = 0, message = "ok", data = _backups.ListBackups() });
    }

    [HttpPost]
    public async Task<IActionResult> Create()
    {
        if (!IsAdmin())
        {
            return StatusCode(403, new { message = "仅管理员可手动备份" });
        }

        try
        {
            var path = await _backups.CreateBackupAsync();
            var list = _backups.ListBackups();
            await _audit.WriteAsync(new AuditLog
            {
                OperatorAccount = User.Identity?.Name,
                OperationType = "backup.manual",
                Target = Path.GetFileName(path),
                Detail = $"平台库手动备份完成，当前保留 {list.Count} 份",
                Result = 1
            });
            return Ok(new { success = true, code = 0, message = "ok", data = new { file = Path.GetFileName(path), retention = list.Count } });
        }
        catch (Exception ex)
        {
            await _audit.WriteAsync(new AuditLog
            {
                OperatorAccount = User.Identity?.Name,
                OperationType = "backup.manual",
                Detail = $"平台库手动备份失败：{ex.Message}",
                Result = 0
            });
            return StatusCode(500, new { message = $"备份失败：{ex.Message}" });
        }
    }

    private bool IsAdmin() =>
        User.FindFirst("accountId") is { } claim &&
        long.TryParse(claim.Value, out _) &&
        (User.IsInRole("admin") || User.HasClaim(c => c.Type == System.Security.Claims.ClaimTypes.Role && c.Value == "admin"));
}
