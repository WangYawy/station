using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Station.Application.Audit;
using Station.Application.Authorization;
using Station.Application.Authentication;
using Station.Contracts.Api;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Infrastructure;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;
using AuthService = Station.Application.Authorization.IAuthorizationService;

namespace Station.Platform.Api.Controllers;

/// <summary>系统管理：组织架构 / 用户 / 角色 / 审计日志（按 RBAC 权限码 + 部门树数据权限）。</summary>
[ApiController]
[Authorize]
[Route("api/v1")]
public class SystemManagementController : ControllerBase
{
    private readonly IRepository<Dept> _depts;
    private readonly IRepository<User> _users;
    private readonly IRepository<Account> _accounts;
    private readonly IRepository<Role> _roles;
    private readonly IRepository<RolePermission> _rolePermissions;
    private readonly IRepository<UserRole> _userRoles;
    private readonly IRepository<Permission> _permissions;
    private readonly IRepository<PlatformStation> _stations;
    private readonly IRepository<AuditLog> _auditLogs;
    private readonly IIdGenerator _idGenerator;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AuthService _authorization;
    private readonly IDataScopeProvider _dataScope;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _audit;

    public SystemManagementController(
        IRepository<Dept> depts,
        IRepository<User> users,
        IRepository<Account> accounts,
        IRepository<Role> roles,
        IRepository<RolePermission> rolePermissions,
        IRepository<UserRole> userRoles,
        IRepository<Permission> permissions,
        IRepository<PlatformStation> stations,
        IRepository<AuditLog> auditLogs,
        IIdGenerator idGenerator,
        IPasswordHasher passwordHasher,
        AuthService authorization,
        IDataScopeProvider dataScope,
        IUnitOfWork uow,
        IAuditLogService audit)
    {
        _depts = depts;
        _users = users;
        _accounts = accounts;
        _roles = roles;
        _rolePermissions = rolePermissions;
        _userRoles = userRoles;
        _permissions = permissions;
        _stations = stations;
        _auditLogs = auditLogs;
        _idGenerator = idGenerator;
        _passwordHasher = passwordHasher;
        _authorization = authorization;
        _dataScope = dataScope;
        _uow = uow;
        _audit = audit;
    }

    // ==================== 组织架构 ====================

    [HttpPost("depts")]
    public async Task<IActionResult> CreateDept(CreateDeptRequest request)
    {
        if (!await RequirePermissionAsync(PermissionCodes.DeptManage))
        {
            return StatusCode(403, new { message = "无部门管理权限" });
        }

        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "部门编码与名称不能为空" });
        }

        if (await _depts.IsAnyAsync(d => d.Code == request.Code.Trim()))
        {
            return BadRequest(new { message = "部门编码已存在" });
        }

        var scope = await GetScopeAsync();
        if (request.ParentId is { } parentId)
        {
            var parent = await _depts.GetByIdAsync(parentId);
            if (parent is null || !parent.IsActive)
            {
                return BadRequest(new { message = "上级部门不存在或已停用" });
            }

            if (!scope.IsAll && !scope.AllowedDeptIds.Contains(parentId))
            {
                return StatusCode(403, new { message = "无权在该部门下创建" });
            }
        }
        else if (!scope.IsAll)
        {
            return StatusCode(403, new { message = "无权创建顶级部门" });
        }

        var dept = new Dept
        {
            Id = _idGenerator.NextId(),
            Code = request.Code.Trim(),
            Name = request.Name.Trim(),
            ParentId = request.ParentId,
            SortOrder = request.SortOrder
        };
        await _depts.InsertAsync(dept);
        await WriteAuditAsync("dept.create", dept.Code, $"新建部门 {dept.Name}");
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPut("depts/{deptId:long}")]
    public async Task<IActionResult> UpdateDept(long deptId, UpdateDeptRequest request)
    {
        if (!await RequirePermissionAsync(PermissionCodes.DeptManage))
        {
            return StatusCode(403, new { message = "无部门管理权限" });
        }

        var dept = await _depts.GetByIdAsync(deptId);
        if (dept is null)
        {
            return NotFound(new { message = "部门不存在" });
        }

        var scope = await GetScopeAsync();
        if (!scope.IsAll && !scope.AllowedDeptIds.Contains(deptId))
        {
            return StatusCode(403, new { message = "无权管理该部门" });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "部门名称不能为空" });
        }

        if (request.ParentId is { } parentId)
        {
            if (parentId == deptId)
            {
                return BadRequest(new { message = "上级部门不能是自身" });
            }

            var parent = await _depts.GetByIdAsync(parentId);
            if (parent is null || !parent.IsActive)
            {
                return BadRequest(new { message = "上级部门不存在或已停用" });
            }

            if (!scope.IsAll && !scope.AllowedDeptIds.Contains(parentId))
            {
                return StatusCode(403, new { message = "无权挂到该上级部门" });
            }

            if (await IsDescendantAsync(parentId, deptId))
            {
                return BadRequest(new { message = "上级部门不能是自身下级" });
            }
        }

        dept.Name = request.Name.Trim();
        dept.ParentId = request.ParentId;
        dept.SortOrder = request.SortOrder;
        dept.IsActive = request.IsActive;
        await _depts.UpdateAsync(dept);
        await WriteAuditAsync("dept.update", dept.Code, $"修改部门 {dept.Name}");
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("depts/{deptId:long}")]
    public async Task<IActionResult> DeleteDept(long deptId)
    {
        if (!await RequirePermissionAsync(PermissionCodes.DeptManage))
        {
            return StatusCode(403, new { message = "无部门管理权限" });
        }

        var dept = await _depts.GetByIdAsync(deptId);
        if (dept is null || !dept.IsActive)
        {
            return NotFound(new { message = "部门不存在" });
        }

        var scope = await GetScopeAsync();
        if (!scope.IsAll && !scope.AllowedDeptIds.Contains(deptId))
        {
            return StatusCode(403, new { message = "无权管理该部门" });
        }

        if (await _depts.IsAnyAsync(d => d.ParentId == deptId && d.IsActive))
        {
            return BadRequest(new { message = "存在子部门，无法删除" });
        }

        if (await _users.IsAnyAsync(u => u.DeptId == deptId && u.IsActive))
        {
            return BadRequest(new { message = "部门下存在用户，无法删除" });
        }

        if (await _stations.IsAnyAsync(s => s.DeptId == deptId))
        {
            return BadRequest(new { message = "部门下存在采集站，无法删除" });
        }

        dept.IsActive = false;
        await _depts.UpdateAsync(dept);
        await WriteAuditAsync("dept.delete", dept.Code, $"停用部门 {dept.Name}");
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ==================== 用户管理 ====================

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers(
        [FromQuery] string? keyword,
        [FromQuery] long? deptId,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20)
    {
        if (!await RequirePermissionAsync(PermissionCodes.UserView))
        {
            return StatusCode(403, new { message = "无用户查看权限" });
        }

        var scope = await GetScopeAsync();
        var query = _users.AsQueryable().Where(u =>
            (deptId == null || u.DeptId == deptId) &&
            (string.IsNullOrWhiteSpace(keyword) || u.UserNo.Contains(keyword) || u.Name.Contains(keyword)));
        if (!scope.IsAll)
        {
            query = query.Where(u => scope.AllowedDeptIds.Contains(u.DeptId));
        }

        var total = query.Count();
        var users = query.OrderBy(u => u.Id, SqlSugar.OrderByType.Asc)
            .ToPageList(Math.Max(1, page), Math.Max(1, size));
        var userIds = users.Select(u => u.Id).ToList();
        var deptIds = users.Select(u => u.DeptId).Distinct().ToList();
        var deptMap = (await _depts.GetListAsync(d => deptIds.Contains(d.Id))).ToDictionary(d => d.Id, d => d.Name);
        var roleNames = await ResolveUserRolesAsync(userIds);
        var accountMap = (await _accounts.GetListAsync(a => a.UserId != null && userIds.Contains(a.UserId.Value)))
            .ToDictionary(a => a.UserId!.Value, a => a.UserName);

        var items = users.Select(u => new UserView(
            u.Id, u.UserNo, u.Name, u.DeptId,
            deptMap.TryGetValue(u.DeptId, out var n) ? n : null,
            accountMap.TryGetValue(u.Id, out var acc) ? acc : null,
            u.IsActive,
            roleNames.TryGetValue(u.Id, out var r) ? r : [])).ToList();
        return Ok(ApiResponse<PagedResult<UserView>>.Ok(new PagedResult<UserView>(page, size, total, items)));
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser(CreateUserRequest request)
    {
        if (!await RequirePermissionAsync(PermissionCodes.UserManage))
        {
            return StatusCode(403, new { message = "无用户管理权限" });
        }

        if (string.IsNullOrWhiteSpace(request.UserNo) || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "工号与姓名不能为空" });
        }

        if (await _users.IsAnyAsync(u => u.UserNo == request.UserNo.Trim()) ||
            await _accounts.IsAnyAsync(a => a.UserName == request.UserNo.Trim()))
        {
            return BadRequest(new { message = "工号或账号已存在" });
        }

        var dept = await _depts.GetByIdAsync(request.DeptId);
        if (dept is null || !dept.IsActive)
        {
            return BadRequest(new { message = "部门不存在或已停用" });
        }

        var scope = await GetScopeAsync();
        if (!scope.IsAll && !scope.AllowedDeptIds.Contains(request.DeptId))
        {
            return StatusCode(403, new { message = "无权在该部门下创建用户" });
        }

        var roleIds = await ValidateRolesAsync(request.RoleIds);
        var password = string.IsNullOrWhiteSpace(request.Password) ? "Station@123" : request.Password;
        await _uow.UseTranAsync(async () =>
        {
            var users = _uow.GetRepository<User>();
            var accounts = _uow.GetRepository<Account>();
            var userRoles = _uow.GetRepository<UserRole>();
            var userId = _idGenerator.NextId();
            await users.InsertAsync(new User
            {
                Id = userId,
                UserNo = request.UserNo.Trim(),
                Name = request.Name.Trim(),
                DeptId = request.DeptId
            });
            await accounts.InsertAsync(new Account
            {
                Id = _idGenerator.NextId(),
                UserName = request.UserNo.Trim(),
                PasswordHash = _passwordHasher.Hash(password),
                UserId = userId
            });
            foreach (var roleId in roleIds)
            {
                await userRoles.InsertAsync(new UserRole { Id = _idGenerator.NextId(), UserId = userId, RoleId = roleId });
            }

            return true;
        });
        await WriteAuditAsync("user.create", request.UserNo.Trim(), $"新建用户 {request.Name.Trim()}");
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPut("users/{userId:long}")]
    public async Task<IActionResult> UpdateUser(long userId, UpdateUserRequest request)
    {
        if (!await RequirePermissionAsync(PermissionCodes.UserManage))
        {
            return StatusCode(403, new { message = "无用户管理权限" });
        }

        var user = await _users.GetByIdAsync(userId);
        if (user is null)
        {
            return NotFound(new { message = "用户不存在" });
        }

        var scope = await GetScopeAsync();
        if (!scope.IsAll && !scope.AllowedDeptIds.Contains(user.DeptId))
        {
            return StatusCode(403, new { message = "无权管理该用户" });
        }

        var dept = await _depts.GetByIdAsync(request.DeptId);
        if (dept is null || !dept.IsActive)
        {
            return BadRequest(new { message = "部门不存在或已停用" });
        }

        if (!scope.IsAll && !scope.AllowedDeptIds.Contains(request.DeptId))
        {
            return StatusCode(403, new { message = "无权将该用户调整到该部门" });
        }

        var roleIds = await ValidateRolesAsync(request.RoleIds);
        await _uow.UseTranAsync(async () =>
        {
            var users = _uow.GetRepository<User>();
            var userRoles = _uow.GetRepository<UserRole>();
            user.Name = request.Name.Trim();
            user.DeptId = request.DeptId;
            user.IsActive = request.IsActive;
            await users.UpdateAsync(user);
            await userRoles.DeleteAsync(ur => ur.UserId == userId);
            foreach (var roleId in roleIds)
            {
                await userRoles.InsertAsync(new UserRole { Id = _idGenerator.NextId(), UserId = userId, RoleId = roleId });
            }

            return true;
        });
        await WriteAuditAsync("user.update", user.UserNo, $"更新用户 {user.Name}");
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPost("users/{userId:long}/reset-password")]
    public async Task<IActionResult> ResetPassword(long userId, ResetPasswordRequest request)
    {
        if (!await RequirePermissionAsync(PermissionCodes.UserManage))
        {
            return StatusCode(403, new { message = "无用户管理权限" });
        }

        var user = await _users.GetByIdAsync(userId);
        if (user is null)
        {
            return NotFound(new { message = "用户不存在" });
        }

        var scope = await GetScopeAsync();
        if (!scope.IsAll && !scope.AllowedDeptIds.Contains(user.DeptId))
        {
            return StatusCode(403, new { message = "无权管理该用户" });
        }

        var account = await _accounts.FirstAsync(a => a.UserId == userId);
        if (account is null)
        {
            return NotFound(new { message = "用户未绑定登录账号" });
        }

        var password = string.IsNullOrWhiteSpace(request.Password) ? "Station@123" : request.Password;
        account.PasswordHash = _passwordHasher.Hash(password);
        account.FailedLoginAttempts = 0;
        account.LockedUntil = null;
        await _accounts.UpdateAsync(account);
        await WriteAuditAsync("user.reset-password", user.UserNo, $"重置用户 {user.Name} 密码");
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ==================== 角色管理 ====================

    [HttpGet("roles")]
    public async Task<IActionResult> ListRoles()
    {
        if (!await RequirePermissionAsync(PermissionCodes.RoleView))
        {
            return StatusCode(403, new { message = "无角色查看权限" });
        }

        var roles = await _roles.GetListAsync(r => r.IsActive);
        var permMap = await ResolveRolePermissionsAsync(roles.Select(r => r.Id).ToList());
        var items = roles.Select(r => new RoleView(
            r.Id, r.Code, r.Name, r.DataScope, r.IsSystem, r.IsActive,
            permMap.TryGetValue(r.Id, out var p) ? p : [])).ToList();
        return Ok(ApiResponse<List<RoleView>>.Ok(items));
    }

    [HttpGet("permissions")]
    public async Task<IActionResult> ListPermissions()
    {
        if (!await RequirePermissionAsync(PermissionCodes.RoleView))
        {
            return StatusCode(403, new { message = "无角色查看权限" });
        }

        var permissions = await _permissions.GetListAsync(p => p.IsActive);
        return Ok(ApiResponse<List<PermissionView>>.Ok(permissions
            .OrderBy(p => p.Module).ThenBy(p => p.Id)
            .Select(p => new PermissionView(p.Id, p.Code, p.Name, p.Module)).ToList()));
    }

    [HttpPost("roles")]
    public async Task<IActionResult> CreateRole(CreateRoleRequest request)
    {
        if (!await RequirePermissionAsync(PermissionCodes.RoleManage))
        {
            return StatusCode(403, new { message = "无角色管理权限" });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "角色名称不能为空" });
        }

        var code = string.IsNullOrWhiteSpace(request.Code)
            ? $"custom_{_idGenerator.NextId()}"
            : request.Code.Trim();
        if (await _roles.IsAnyAsync(r => r.Code == code))
        {
            return BadRequest(new { message = "角色编码已存在" });
        }

        var permissionIds = await ResolvePermissionIdsAsync(request.PermissionCodes);
        await _uow.UseTranAsync(async () =>
        {
            var roles = _uow.GetRepository<Role>();
            var rolePermissions = _uow.GetRepository<RolePermission>();
            var roleId = _idGenerator.NextId();
            await roles.InsertAsync(new Role
            {
                Id = roleId,
                Code = code,
                Name = request.Name.Trim(),
                DataScope = request.DataScope
            });
            foreach (var permissionId in permissionIds)
            {
                await rolePermissions.InsertAsync(new RolePermission
                {
                    Id = _idGenerator.NextId(),
                    RoleId = roleId,
                    PermissionId = permissionId
                });
            }

            return true;
        });
        await WriteAuditAsync("role.create", code, $"新建角色 {request.Name.Trim()}");
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPut("roles/{roleId:long}")]
    public async Task<IActionResult> UpdateRole(long roleId, UpdateRoleRequest request)
    {
        if (!await RequirePermissionAsync(PermissionCodes.RoleManage))
        {
            return StatusCode(403, new { message = "无角色管理权限" });
        }

        var role = await _roles.GetByIdAsync(roleId);
        if (role is null)
        {
            return NotFound(new { message = "角色不存在" });
        }

        if (role.IsSystem)
        {
            return BadRequest(new { message = "预置角色不可修改" });
        }

        var permissionIds = await ResolvePermissionIdsAsync(request.PermissionCodes);
        await _uow.UseTranAsync(async () =>
        {
            var roles = _uow.GetRepository<Role>();
            var rolePermissions = _uow.GetRepository<RolePermission>();
            role.Name = request.Name.Trim();
            role.DataScope = request.DataScope;
            role.IsActive = request.IsActive;
            await roles.UpdateAsync(role);
            await rolePermissions.DeleteAsync(rp => rp.RoleId == roleId);
            foreach (var permissionId in permissionIds)
            {
                await rolePermissions.InsertAsync(new RolePermission
                {
                    Id = _idGenerator.NextId(),
                    RoleId = roleId,
                    PermissionId = permissionId
                });
            }

            return true;
        });
        await WriteAuditAsync("role.update", role.Code, $"修改角色 {role.Name}");
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ==================== 审计日志 ====================

    [HttpGet("audit-logs")]
    public async Task<IActionResult> ListAuditLogs(
        [FromQuery] string? keyword,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20)
    {
        if (!await RequirePermissionAsync(PermissionCodes.AuditView))
        {
            return StatusCode(403, new { message = "无审计查看权限" });
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

        var total = query.Count();
        var items = query.OrderBy(a => a.CreatedAt, SqlSugar.OrderByType.Desc)
            .ToPageList(Math.Max(1, page), Math.Max(1, size))
            .Select(a => new AuditLogView(
                a.Id, a.OperatorAccount, a.OperatorName, a.DeptId, a.SourceIp,
                a.OperationType, a.Target, a.Detail, a.Result, a.CreatedAt))
            .ToList();
        return Ok(ApiResponse<PagedResult<AuditLogView>>.Ok(new PagedResult<AuditLogView>(page, size, total, items)));
    }

    // ==================== 私有辅助 ====================

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

    private async Task<bool> IsDescendantAsync(long nodeId, long ancestorId)
    {
        var current = nodeId;
        var visited = new HashSet<long>();
        while (current != 0 && visited.Add(current))
        {
            var dept = await _depts.GetByIdAsync(current);
            if (dept is null)
            {
                return false;
            }

            if (dept.ParentId == ancestorId)
            {
                return true;
            }

            current = dept.ParentId ?? 0;
        }

        return false;
    }

    private async Task<List<long>> ValidateRolesAsync(IReadOnlyList<long> roleIds)
    {
        var distinct = roleIds.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return [];
        }

        var active = await _roles.GetListAsync(r => distinct.Contains(r.Id) && r.IsActive);
        return active.Select(r => r.Id).ToList();
    }

    private async Task<List<long>> ResolvePermissionIdsAsync(IReadOnlyList<string> codes)
    {
        var distinct = codes.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return [];
        }

        var active = await _permissions.GetListAsync(p => distinct.Contains(p.Code) && p.IsActive);
        return active.Select(p => p.Id).ToList();
    }

    private async Task<Dictionary<long, List<string>>> ResolveUserRolesAsync(List<long> userIds)
    {
        var result = new Dictionary<long, List<string>>();
        if (userIds.Count == 0)
        {
            return result;
        }

        var links = await _userRoles.GetListAsync(ur => userIds.Contains(ur.UserId));
        var roleIds = links.Select(x => x.RoleId).Distinct().ToList();
        var roles = await _roles.GetListAsync(r => roleIds.Contains(r.Id));
        var roleNameMap = roles.ToDictionary(r => r.Id, r => r.Name);
        foreach (var link in links)
        {
            if (!result.TryGetValue(link.UserId, out var list))
            {
                list = [];
                result[link.UserId] = list;
            }

            if (roleNameMap.TryGetValue(link.RoleId, out var name))
            {
                list.Add(name);
            }
        }

        return result;
    }

    private async Task<Dictionary<long, List<string>>> ResolveRolePermissionsAsync(List<long> roleIds)
    {
        var result = new Dictionary<long, List<string>>();
        if (roleIds.Count == 0)
        {
            return result;
        }

        var links = await _rolePermissions.GetListAsync(rp => roleIds.Contains(rp.RoleId));
        var permissionIds = links.Select(x => x.PermissionId).Distinct().ToList();
        var permissions = await _permissions.GetListAsync(p => permissionIds.Contains(p.Id));
        var codeMap = permissions.ToDictionary(p => p.Id, p => p.Code);
        foreach (var link in links)
        {
            if (!result.TryGetValue(link.RoleId, out var list))
            {
                list = [];
                result[link.RoleId] = list;
            }

            if (codeMap.TryGetValue(link.PermissionId, out var code))
            {
                list.Add(code);
            }
        }

        return result;
    }

    private async Task WriteAuditAsync(string operationType, string? target, string? detail)
    {
        var session = await CurrentSessionAsync();
        await _audit.WriteAsync(new AuditLog
        {
            OperatorAccount = session?.UserName,
            OperatorName = session?.Name,
            OperatorUserId = session?.UserId,
            DeptId = session?.DeptId,
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            OperationType = operationType,
            Target = target,
            Detail = detail,
            Result = 1
        });
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
}

public sealed record CreateDeptRequest(string Code, string Name, long? ParentId, int SortOrder = 0);

public sealed record UpdateDeptRequest(string Name, long? ParentId, int SortOrder, bool IsActive = true);

public sealed record CreateUserRequest(
    string UserNo,
    string Name,
    long DeptId,
    string? Password,
    IReadOnlyList<long> RoleIds);

public sealed record UpdateUserRequest(
    string Name,
    long DeptId,
    bool IsActive,
    IReadOnlyList<long> RoleIds);

public sealed record ResetPasswordRequest(string? Password);

public sealed record CreateRoleRequest(
    string? Code,
    string Name,
    DataScope DataScope,
    IReadOnlyList<string> PermissionCodes);

public sealed record UpdateRoleRequest(
    string Name,
    DataScope DataScope,
    bool IsActive,
    IReadOnlyList<string> PermissionCodes);

public sealed record UserView(
    long Id,
    string UserNo,
    string Name,
    long DeptId,
    string? DeptName,
    string? AccountName,
    bool IsActive,
    IReadOnlyList<string> Roles);

public sealed record RoleView(
    long Id,
    string Code,
    string Name,
    DataScope DataScope,
    bool IsSystem,
    bool IsActive,
    IReadOnlyList<string> Permissions);

public sealed record PermissionView(long Id, string Code, string Name, string Module);

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
