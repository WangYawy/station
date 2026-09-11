using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Station.Application.Audit;
using Station.Application.Alerts;
using Station.Application.Authorization;
using Station.Application.Recorders;
using Station.Application.Users;
using Station.Contracts;
using Station.Domain.Entities;
using Station.Domain.Repositories;
using AuthService = Station.Application.Authorization.IAuthorizationService;
using Station.Domain.Collecting;

namespace Station.Desktop.WebHost.Controllers;

/// <summary>单机 Web 管理：部门 / 用户 / 角色 / 记录仪 / 报警（RBAC + 数据范围）。</summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public class StandaloneAdminController : ControllerBase
{
    private readonly IUserService _users;
    private readonly IRecorderService _recorders;
    private readonly IAlertService _alerts;
    private readonly ICollectSourceProvider _sourceProvider;
    private readonly IRepository<Account> _accounts;
    private readonly IAuditLogService _audit;
    private readonly AuthService _authorization;
    private readonly IDataScopeProvider _dataScope;

    public StandaloneAdminController(
        IUserService users,
        IRecorderService recorders,
        IAlertService alerts,
        ICollectSourceProvider sourceProvider,
        IRepository<Account> accounts,
        IAuditLogService audit,
        AuthService authorization,
        IDataScopeProvider dataScope)
    {
        _users = users;
        _recorders = recorders;
        _alerts = alerts;
        _sourceProvider = sourceProvider;
        _accounts = accounts;
        _audit = audit;
        _authorization = authorization;
        _dataScope = dataScope;
    }

    // ==================== 部门 ====================

    [HttpGet("depts")]
    public async Task<IActionResult> ListDepts()
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.DeptView))
        {
            return StatusCode(403, new { message = "无部门查看权限" });
        }

        var scope = await WebScopeHelper.GetScopeAsync(User, _authorization, _dataScope);
        var depts = (await _users.GetDeptTreeAsync())
            .Where(d => d.Id is { } id && (scope.IsAll || scope.AllowedDeptIds.Contains(id)))
            .ToList();
        return Ok(ApiOk(depts));
    }

    [HttpPost("depts")]
    public async Task<IActionResult> CreateDept(DeptDto dto)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.DeptManage))
        {
            return StatusCode(403, new { message = "无部门管理权限" });
        }

        return Ok(ApiOk(await _users.CreateDeptAsync(dto with { Id = null })));
    }

    [HttpPut("depts/{deptId:long}")]
    public async Task<IActionResult> UpdateDept(long deptId, DeptDto dto)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.DeptManage))
        {
            return StatusCode(403, new { message = "无部门管理权限" });
        }

        var result = await _users.UpdateDeptAsync(dto with { Id = deptId });
        return result.Success ? Ok(ApiOk(true)) : BadRequest(new { message = result.Message });
    }

    [HttpDelete("depts/{deptId:long}")]
    public async Task<IActionResult> DeleteDept(long deptId)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.DeptManage))
        {
            return StatusCode(403, new { message = "无部门管理权限" });
        }

        var result = await _users.DeleteDeptAsync(deptId);
        return result.Success ? Ok(ApiOk(true)) : BadRequest(new { message = result.Message });
    }

    // ==================== 用户 ====================

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers()
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.UserView))
        {
            return StatusCode(403, new { message = "无用户查看权限" });
        }

        var scope = await WebScopeHelper.GetScopeAsync(User, _authorization, _dataScope);
        var users = (await _users.GetUsersAsync())
            .Where(u => scope.IsAll || scope.AllowedDeptIds.Contains(u.DeptId))
            .ToList();
        return Ok(ApiOk(users));
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserWebRequest request)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.UserManage))
        {
            return StatusCode(403, new { message = "无用户管理权限" });
        }

        var dto = new UserDto(null, request.UserNo, request.Name, request.DeptId);
        var result = await _users.CreateUserAsync(dto, request.UserNo, request.Password ?? "Station@123");
        return result.Success ? Ok(ApiOk(true)) : BadRequest(new { message = result.Message });
    }

    [HttpPut("users/{userId:long}")]
    public async Task<IActionResult> UpdateUser(long userId, UserDto dto)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.UserManage))
        {
            return StatusCode(403, new { message = "无用户管理权限" });
        }

        var result = await _users.UpdateUserAsync(dto with { Id = userId });
        return result.Success ? Ok(ApiOk(true)) : BadRequest(new { message = result.Message });
    }

    [HttpPut("users/{userId:long}/roles")]
    public async Task<IActionResult> AssignRoles(long userId, [FromBody] RoleIdsRequest request)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.UserAssignRole))
        {
            return StatusCode(403, new { message = "无角色分配权限" });
        }

        var result = await _users.AssignRolesAsync(userId, request.RoleIds);
        return result.Success ? Ok(ApiOk(true)) : BadRequest(new { message = result.Message });
    }

    [HttpPost("users/{userId:long}/reset-password")]
    public async Task<IActionResult> ResetPassword(long userId, [FromBody] ResetPasswordRequest request)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.UserManage))
        {
            return StatusCode(403, new { message = "无用户管理权限" });
        }

        var account = await _accounts.FirstAsync(a => a.UserId == userId);
        if (account is null)
        {
            return NotFound(new { message = "用户未绑定登录账号" });
        }

        var result = await _users.ResetPasswordAsync(account.Id, request.Password);
        return result.Success ? Ok(ApiOk(true)) : BadRequest(new { message = result.Message });
    }

    // ==================== 角色 ====================

    [HttpGet("roles")]
    public async Task<IActionResult> ListRoles()
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.RoleView))
        {
            return StatusCode(403, new { message = "无角色查看权限" });
        }

        return Ok(ApiOk(await _users.GetRolesAsync()));
    }

    [HttpGet("permissions")]
    public async Task<IActionResult> ListPermissions()
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.RoleView))
        {
            return StatusCode(403, new { message = "无角色查看权限" });
        }

        return Ok(ApiOk(await _users.GetPermissionCatalogAsync()));
    }

    [HttpPost("roles")]
    public async Task<IActionResult> CreateRole(RoleDto dto)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.RoleManage))
        {
            return StatusCode(403, new { message = "无角色管理权限" });
        }

        return Ok(ApiOk(await _users.CreateRoleAsync(dto with { Id = null })));
    }

    [HttpPut("roles/{roleId:long}")]
    public async Task<IActionResult> UpdateRole(long roleId, RoleDto dto)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.RoleManage))
        {
            return StatusCode(403, new { message = "无角色管理权限" });
        }

        var result = await _users.UpdateRoleAsync(dto with { Id = roleId });
        return result.Success ? Ok(ApiOk(true)) : BadRequest(new { message = result.Message });
    }

    [HttpPut("roles/{roleId:long}/permissions")]
    public async Task<IActionResult> SetRolePermissions(long roleId, [FromBody] PermissionIdsRequest request)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.RoleManage))
        {
            return StatusCode(403, new { message = "无角色管理权限" });
        }

        var result = await _users.SetRolePermissionsAsync(roleId, request.PermissionIds);
        return result.Success ? Ok(ApiOk(true)) : BadRequest(new { message = result.Message });
    }

    // ==================== 记录仪 ====================

    [HttpGet("recorders")]
    public async Task<IActionResult> ListRecorders()
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.RecorderView))
        {
            return StatusCode(403, new { message = "无记录仪查看权限" });
        }

        return Ok(ApiOk(await _recorders.GetRecordersAsync()));
    }

    /// <summary>写入绑定：更新台账绑定并写入已连接记录仪根目录 ini（UMS=可移动磁盘根，模拟源=模拟目录）。</summary>
    [HttpPut("recorders/{recorderId:long}/bind")]
    public async Task<IActionResult> BindRecorder(long recorderId, [FromBody] BindRecorderWebRequest request)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.RecorderManage))
        {
            return StatusCode(403, new { message = "无记录仪管理权限" });
        }

        var recorder = (await _recorders.GetRecordersAsync()).FirstOrDefault(r => r.Id == recorderId);
        if (recorder is null)
        {
            return NotFound(new { message = "记录仪不存在" });
        }

        try
        {
            var root = _sourceProvider.GetFor(recorder.Protocol).GetRecorderRoot(
                new CollectDeviceInfo(recorder.SerialNumber, recorder.SerialNumber, recorder.Protocol));
            await _recorders.WriteBindingAsync(recorder.SerialNumber, request.UserId, request.DeptId, root);
            await _audit.WriteAsync(new AuditLog
            {
                OperatorAccount = User.Identity?.Name,
                OperationType = "recorder.bind",
                Target = recorder.SerialNumber,
                Detail = $"写入绑定 userId={request.UserId}, deptId={request.DeptId}",
                SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                Result = 1
            });
            return Ok(ApiOk(true));
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = $"写入绑定失败：{ex.Message}" });
        }
    }

    // ==================== 报警 ====================

    [HttpGet("alerts")]
    public async Task<IActionResult> ListAlerts(
        [FromQuery] AlertLevel? level,
        [FromQuery] AlertStatus? status,
        [FromQuery] int count = 100)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.AlertView))
        {
            return StatusCode(403, new { message = "无报警查看权限" });
        }

        return Ok(ApiOk(await _alerts.GetAlertsAsync(level, status, count)));
    }

    [HttpPost("alerts/{alertId:long}/status")]
    public async Task<IActionResult> SetAlertStatus(long alertId, [FromBody] AlertStatusRequest request)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.AlertHandle))
        {
            return StatusCode(403, new { message = "无报警处置权限" });
        }

        return Ok(ApiOk(await _alerts.SetStatusAsync(alertId, request.Status)));
    }

    // ==================== 辅助 ====================

    private async Task<bool> RequirePermissionAsync(string code)
    {
        if (User.FindFirst("accountId") is not { } accountClaim ||
            !long.TryParse(accountClaim.Value, out var accountId))
        {
            return false;
        }

        return await _authorization.HasPermissionAsync(accountId, code);
    }

    private static object ApiOk(object? data) => new { success = true, code = 0, message = "ok", data };
}

public sealed record CreateUserWebRequest(string UserNo, string Name, long DeptId, string? Password);

public sealed record RoleIdsRequest(IReadOnlyList<long> RoleIds);

public sealed record ResetPasswordRequest(string Password);

public sealed record PermissionIdsRequest(IReadOnlyList<long> PermissionIds);

public sealed record BindRecorderWebRequest(long? UserId, long? DeptId);

public sealed record AlertStatusRequest(AlertStatus Status);
