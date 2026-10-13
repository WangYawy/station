using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Station.Application.Audit;
using Station.Application.Alerts;
using Station.Application.Authorization;
using Station.Application.Recorders;
using Station.Application.Users;
using Station.Contracts;
using Station.Domain.Entities;
using AuthService = Station.Application.Authorization.IAuthorizationService;
using Station.Domain.Collecting;
using Station.Domain.Accounts;
using Station.Application.Departments;
using Station.Application.Departments.Commands;
using Station.Application.Users.Commands;
using Station.Application.Roles;
using Station.Application.Roles.Commands;
using Station.Application.Accounts;

namespace Station.Desktop.WebHost.Controllers;

/// <summary>单机 Web 管理：部门 / 用户 / 角色 / 记录仪 / 报警（RBAC + 数据范围）。</summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public class StandaloneAdminController : ControllerBase
{
    private readonly IUserService _users;
    private readonly IUserQueryService _userQuery;
    private readonly IDeptService _depts;
    private readonly IDeptQueryService _deptQuery;
    private readonly IRoleService _roles;
    private readonly IRoleQueryService _roleQuery;
    //private readonly IPermissionService _permissions;
    //private readonly IRoleQueryService _roleQuery;
    private readonly IAccountService _accounts;
    private readonly IAccountQueryService _accountQuery;

    private readonly IRecorderService _recorders;
    private readonly IAlertService _alerts;
    private readonly ICollectSourceProvider _sourceProvider;

    private readonly IAuditLogService _audit;
    private readonly AuthService _authorization;
    private readonly IDataScopeProvider _dataScope;

    public StandaloneAdminController(
        IUserService users,
        IUserQueryService userQuery,
        IDeptService depts,
        IDeptQueryService deptQuery,
        IRoleService roles,
        IRoleQueryService roleQuery,
        IAccountService accounts,
        IAccountQueryService accountQuery,
        IRecorderService recorders,
        IAlertService alerts,
        ICollectSourceProvider sourceProvider,
        IAuditLogService audit,
        AuthService authorization,
        IDataScopeProvider dataScope)
    {
        _users = users;
        _userQuery = userQuery;
        _depts = depts;
        _deptQuery = deptQuery;
        _roles = roles;
        _roleQuery = roleQuery;
        _accounts = accounts;
        _accountQuery = accountQuery;
        _recorders = recorders;
        _alerts = alerts;
        _sourceProvider = sourceProvider;
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
        var depts = (await _deptQuery.GetTreeAsync())
            .Where(d => d.Id is { } id && (scope.IsAll || scope.AllowedDeptIds.Contains(id)))
            .ToList();
        return Ok(ApiOk(depts));
    }

    [HttpPost("depts")]
    public async Task<IActionResult> CreateDept(CreateDeptCommand dto)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.DeptManage))
        {
            return StatusCode(403, new { message = "无部门管理权限" });
        }

        return Ok(ApiOk(await _depts.CreateAsync(dto)));
    }

    [HttpPut("depts/{deptId:long}")]
    public async Task<IActionResult> UpdateDept(UpdateDeptCommand dto)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.DeptManage))
        {
            return StatusCode(403, new { message = "无部门管理权限" });
        }

        var result = await _depts.UpdateAsync(dto);
        return result.Success ? Ok(ApiOk(true)) : BadRequest(new { message = result.Message });
    }

    [HttpDelete("depts/{deptId:long}")]
    public async Task<IActionResult> DeleteDept(long deptId)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.DeptManage))
        {
            return StatusCode(403, new { message = "无部门管理权限" });
        }

        var result = await _depts.DeleteAsync(deptId);
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
        var users = (await _userQuery.PageAsync(new Application.Users.Dtos.UserPageQuery { })).Items
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

        var user = new CreateUserCommand
        {
            UserNo = request.UserNo,
            UserName = request.Name,
            DeptId = request.DeptId,
            Password = request.Password ?? "Station@123",
            IsActive = true
        };
        var result = await _users.CreateAsync(user);
        return result.Success ? Ok(ApiOk(true)) : BadRequest(new { message = result.Message });
    }

    [HttpPut("users/{userId:long}")]
    public async Task<IActionResult> UpdateUser(UpdateUserCommand dto)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.UserManage))
        {
            return StatusCode(403, new { message = "无用户管理权限" });
        }

        var result = await _users.UpdateAsync(dto);
        return result.Success ? Ok(ApiOk(true)) : BadRequest(new { message = result.Message });
    }

    [HttpPut("users/{userId:long}/roles")]
    public async Task<IActionResult> AssignRoles(long userId, [FromBody] RoleIdsRequest request)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.UserAssignRole))
        {
            return StatusCode(403, new { message = "无角色分配权限" });
        }

        var result = await _users.AssignRolesAsync(new AssignUserRolesCommand(userId, request.RoleIds));
        return result.Success ? Ok(ApiOk(true)) : BadRequest(new { message = result.Message });
    }

    [HttpPost("users/{userId:long}/reset-password")]
    public async Task<IActionResult> ResetPassword(long userId, [FromBody] ResetPasswordRequest request)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.UserManage))
        {
            return StatusCode(403, new { message = "无用户管理权限" });
        }

        var accounts = await _accountQuery.GetByUserIdAsync(userId);
        if (accounts is null || accounts.Count == 0)
        {
            return NotFound(new { message = "用户未绑定登录账号" });
        }
        var account = accounts.First();
        var result = await _accounts.ResetPasswordAsync(new Application.Accounts.Commands.ResetPasswordCommand(account.Id, request.Password));
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

        return Ok(ApiOk(await _roleQuery.PageAsync(new Application.Roles.Dtos.RolePageQuery { })));
    }

    [HttpGet("permissions")]
    public async Task<IActionResult> ListPermissions()
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.RoleView))
        {
            return StatusCode(403, new { message = "无角色查看权限" });
        }

        return Ok(ApiOk(await _roleQuery.PageAsync(new Application.Roles.Dtos.RolePageQuery { })));
    }

    [HttpPost("roles")]
    public async Task<IActionResult> CreateRole(CreateRoleCommand dto)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.RoleManage))
        {
            return StatusCode(403, new { message = "无角色管理权限" });
        }

        return Ok(ApiOk(await _roles.CreateAsync(dto)));
    }

    [HttpPut("roles/{roleId:long}")]
    public async Task<IActionResult> UpdateRole(UpdateRoleCommand dto)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.RoleManage))
        {
            return StatusCode(403, new { message = "无角色管理权限" });
        }

        var result = await _roles.UpdateAsync(dto);
        return result.Success ? Ok(ApiOk(true)) : BadRequest(new { message = result.Message });
    }

    [HttpPut("roles/{roleId:long}/permissions")]
    public async Task<IActionResult> SetRolePermissions(long roleId, [FromBody] PermissionIdsRequest request)
    {
        if (!await RequirePermissionAsync(Domain.Authorization.PermissionCodes.RoleManage))
        {
            return StatusCode(403, new { message = "无角色管理权限" });
        }

        var result = await _roles.SetPermissionsAsync(new SetRolePermissionsCommand(roleId, request.PermissionIds));
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
