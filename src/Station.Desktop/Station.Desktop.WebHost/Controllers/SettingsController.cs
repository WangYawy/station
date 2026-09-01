using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Station.Application.Audit;
using Station.Application.Authorization;
using Station.Application.Exporting;
using Station.Application.Licensing;
using Station.Application.Settings;
using Station.Desktop.Application.Settings;
using Station.Desktop.WebHost.Settings;
using Station.Domain.Entities;
using AuthService = Station.Application.Authorization.IAuthorizationService;

namespace Station.Desktop.WebHost.Controllers;

/// <summary>
/// 系统设置：基本/存储/采集/授权/网络/设备自检（对应单机版 Web 原型"设置"页）。
/// 查看需 setting:view，修改需 setting:manage；平台版为只读。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/settings")]
public class SettingsController : ControllerBase
{
    private readonly ISystemSettingsService _settings;
    private readonly INetworkSettingsService _network;
    private readonly ISystemSelfCheckService _selfCheck;
    private readonly ILicenseService _license;
    private readonly IAuditLogService _audit;
    private readonly AuthService _authorization;

    public SettingsController(
        ISystemSettingsService settings,
        INetworkSettingsService network,
        ISystemSelfCheckService selfCheck,
        ILicenseService license,
        IAuditLogService audit,
        AuthService authorization)
    {
        _settings = settings;
        _network = network;
        _selfCheck = selfCheck;
        _license = license;
        _audit = audit;
        _authorization = authorization;
    }

    [HttpGet]
    public async Task<IActionResult> GetSettings()
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.SettingView))
        {
            return StatusCode(403, new { message = "无设置查看权限" });
        }

        var core = await _settings.GetCoreAsync();
        var network = _network.Get();
        return Ok(ApiOk(new
        {
            core.Basic,
            core.Storage,
            core.Collect,
            core.Workbench,
            core.License,
            Network = network,
            core.ReadOnly
        }));
    }

    [HttpPut("{group}")]
    public async Task<IActionResult> UpdateSettings(
        string group,
        [FromBody] SystemSettingsUpdateRequest request)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.SettingManage))
        {
            return StatusCode(403, new { message = "无设置修改权限" });
        }

        var core = await _settings.GetCoreAsync();
        if (core.ReadOnly)
        {
            return StatusCode(403, new { message = "平台版配置由平台统一下发，本地只读" });
        }

        try
        {
            var hints = group switch
            {
                "network" => _network.Update(request.Values),
                "basic" or "storage" or "collect" or "workbench" => await _settings.UpdateAsync(group, request.Values, User.Identity?.Name),
                _ => throw new ArgumentException($"未知设置分组: {group}")
            };
            await WriteAuditAsync("settings.update", group, $"更新 {string.Join(", ", request.Values.Keys)}", 1);
            return Ok(ApiOk(new { hints }));
        }
        catch (Exception ex)
        {
            await WriteAuditAsync("settings.update", group, $"更新失败：{ex.Message}", 0);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("license/activate")]
    public async Task<IActionResult> ActivateLicense([FromBody] string licenseFileText)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.SettingManage))
        {
            return StatusCode(403, new { message = "无设置修改权限" });
        }

        var (ok, message) = await _license.ActivateAsync(licenseFileText);
        await WriteAuditAsync("license.activate", "license", message, ok ? 1 : 0);
        return ok ? Ok(ApiOk(new { message })) : BadRequest(new { message });
    }

    [HttpPost("certificate")]
    public async Task<IActionResult> UploadCertificate([FromBody] CertificateUploadRequest request)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.SettingManage))
        {
            return StatusCode(403, new { message = "无设置修改权限" });
        }

        var (ok, message) = _network.UploadCertificate(request.FileName, request.Base64, request.Password ?? string.Empty);
        await WriteAuditAsync("network.certificate", "network", message, ok ? 1 : 0);
        return ok ? Ok(ApiOk(new { message })) : BadRequest(new { message });
    }

    [HttpPost("self-check")]
    public async Task<IActionResult> RunSelfCheck()
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.SettingView))
        {
            return StatusCode(403, new { message = "无设置查看权限" });
        }

        var items = await _selfCheck.RunAsync();
        await WriteAuditAsync("settings.selfcheck", "selfcheck", $"自检完成：{items.Count(i => i.Ok)}/{items.Count} 项正常", 1);
        return Ok(ApiOk(items));
    }

    [HttpGet("self-check/report")]
    public async Task<IActionResult> DownloadSelfCheckReport(string format = "pdf")
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.SettingView))
        {
            return StatusCode(403, new { message = "无设置查看权限" });
        }

        var items = await _selfCheck.RunAsync();
        var rows = items.Select(i => new[] { i.Name, i.Ok ? "正常" : "异常", i.Detail }).ToList();
        var bytes = ExportDocumentBuilder.Build(
            format,
            "设备自检报告",
            ["检查项", "结果", "详情"],
            rows);
        var ext = format.ToLowerInvariant() switch { "xlsx" => "xlsx", "pdf" => "pdf", _ => "csv" };
        await WriteAuditAsync("settings.selfcheck", "selfcheck", $"导出自检报告（{ext}）", 1);
        return File(bytes, "application/octet-stream", $"设备自检报告.{ext}");
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

    private async Task WriteAuditAsync(string operationType, string target, string detail, int result)
    {
        await _audit.WriteAsync(new AuditLog
        {
            OperatorAccount = User.Identity?.Name,
            OperationType = operationType,
            Target = target,
            Detail = detail,
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Result = result
        });
    }

    private static object ApiOk(object? data) => new { success = true, code = 0, message = "ok", data };
}

public sealed record CertificateUploadRequest(string FileName, string Base64, string? Password);
