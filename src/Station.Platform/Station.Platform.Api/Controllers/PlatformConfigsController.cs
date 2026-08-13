using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Station.Contracts;
using Station.Contracts.Api;
using Station.Contracts.Sync;
using Station.Application.Audit;
using Station.Application.Authorization;
using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Repositories;
using Station.Platform.Domain.Entities;
using AuthService = Station.Application.Authorization.IAuthorizationService;

namespace Station.Platform.Api.Controllers;

/// <summary>平台配置发布与查询。</summary>
[ApiController]
[Route("api/v1/stations/{stationId:long}/configs")]
public class PlatformConfigsController : ControllerBase
{
    private readonly IRepository<PlatformConfigChange> _changes;
    private readonly IIdGenerator _idGenerator;
    private readonly IRepository<Dept> _depts;
    private readonly IRepository<User> _users;
    private readonly IRepository<Account> _accounts;
    private readonly IRepository<Role> _roles;
    private readonly IRepository<RolePermission> _rolePermissions;
    private readonly IRepository<UserRole> _userRoles;
    private readonly IRepository<Permission> _permissions;
    private readonly IRepository<PlatformRecorder> _recorders;
    private readonly IRepository<PlatformStation> _stations;
    private readonly AuthService _authorization;
    private readonly IAuditLogService _audit;

    public PlatformConfigsController(
        IRepository<PlatformConfigChange> changes,
        IIdGenerator idGenerator,
        IRepository<Dept> depts,
        IRepository<User> users,
        IRepository<Account> accounts,
        IRepository<Role> roles,
        IRepository<RolePermission> rolePermissions,
        IRepository<UserRole> userRoles,
        IRepository<Permission> permissions,
        IRepository<PlatformRecorder> recorders,
        IRepository<PlatformStation> stations,
        AuthService authorization,
        IAuditLogService audit)
    {
        _changes = changes;
        _idGenerator = idGenerator;
        _depts = depts;
        _users = users;
        _accounts = accounts;
        _roles = roles;
        _rolePermissions = rolePermissions;
        _userRoles = userRoles;
        _permissions = permissions;
        _recorders = recorders;
        _stations = stations;
        _authorization = authorization;
        _audit = audit;
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<long>>> Publish(
        long stationId,
        PublishConfigRequest request)
    {
        var existing = await _changes.GetListAsync(c => c.StationId == stationId);
        var nextVersion = existing.Count == 0 ? 1 : existing.Max(c => c.Version) + 1;
        await _changes.InsertAsync(new PlatformConfigChange
        {
            Id = _idGenerator.NextId(),
            StationId = stationId,
            EntityType = request.EntityType,
            Operation = request.Operation,
            PayloadJson = request.PayloadJson,
            Version = nextVersion,
            PublishedAt = DateTime.Now
        });
        return Ok(ApiResponse<long>.Ok(nextVersion));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ConfigChangeItem>>>> List(long stationId)
    {
        var list = (await _changes.GetListAsync(c => c.StationId == stationId))
            .OrderBy(c => c.Version)
            .Select(ToItem)
            .ToList();
        return Ok(ApiResponse<List<ConfigChangeItem>>.Ok(list));
    }

    /// <summary>
    /// 一键发布用户域全量快照（组织/用户/角色/用户角色/账号/记录仪白名单）。
    /// 每个实体类型写入一条 Upsert 变更（全量快照），按依赖顺序分配递增版本；
    /// 采集站本地按自然键 upsert，缺失行软停用，保证"以平台为准、本地只读"。
    /// </summary>
    [Authorize]
    [HttpPost("sync-domain")]
    public async Task<IActionResult> PublishDomain(long stationId)
    {
        if (User.FindFirst("accountId") is not { } accountClaim ||
            !await _authorization.HasPermissionAsync(long.Parse(accountClaim.Value), PermissionCodes.UserManage))
        {
            return StatusCode(403, new { message = "无用户域配置下发权限" });
        }

        var station = await _stations.GetByIdAsync(stationId);
        if (station is null)
        {
            return NotFound(new { message = "采集站不存在" });
        }

        var depts = await _depts.GetListAsync(d => d.IsActive);
        var deptCodeById = depts.ToDictionary(d => d.Id, d => d.Code);
        var users = await _users.GetListAsync(u => u.IsActive);
        var userNoById = users.ToDictionary(u => u.Id, u => u.UserNo);
        var accounts = await _accounts.GetListAsync();
        var roles = await _roles.GetListAsync(r => r.IsActive);
        var permissions = await _permissions.GetListAsync(p => p.IsActive);
        var permissionCodeById = permissions.ToDictionary(p => p.Id, p => p.Code);
        var rolePermissions = await _rolePermissions.GetListAsync();
        var rolePermByRole = rolePermissions
            .GroupBy(x => x.RoleId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => permissionCodeById.TryGetValue(x.PermissionId, out var code) ? code : null)
                    .Where(code => code is not null)
                    .Cast<string>()
                    .ToList());
        var userRoles = await _userRoles.GetListAsync();
        var roleCodeById = roles.ToDictionary(r => r.Id, r => r.Code);
        var recorders = await _recorders.GetListAsync(r =>
            r.IsActive && (r.IsWhitelisted || r.BoundUserNo != null));

        var deptRows = depts
            .Select(d => new DeptSyncRow(
                d.Code,
                d.Name,
                d.ParentId is { } parentId && deptCodeById.TryGetValue(parentId, out var parentCode)
                    ? parentCode
                    : null,
                d.SortOrder,
                d.IsActive))
            .ToList();
        var userRows = users
            .Select(u => new UserSyncRow(
                u.UserNo,
                u.Name,
                deptCodeById.TryGetValue(u.DeptId, out var deptCode) ? deptCode : AuthSeedData.RootDeptCode,
                u.IsActive))
            .ToList();
        var roleRows = roles
            .Select(r => new RoleSyncRow(
                r.Code,
                r.Name,
                (int)r.DataScope,
                r.IsSystem,
                r.IsActive,
                rolePermByRole.TryGetValue(r.Id, out var codes) ? codes : []))
            .ToList();
        var userRoleRows = userRoles
            .Where(ur => userNoById.TryGetValue(ur.UserId, out var _) &&
                         roleCodeById.TryGetValue(ur.RoleId, out var _))
            .Select(ur => new UserRoleSyncRow(userNoById[ur.UserId], roleCodeById[ur.RoleId]))
            .ToList();
        var accountRows = accounts
            .Select(a => new AccountSyncRow(
                a.UserName,
                a.PasswordHash,
                a.UserId is { } userId && userNoById.TryGetValue(userId, out var userNo) ? userNo : null,
                a.IsEnabled,
                a.FailedLoginAttempts,
                a.LockedUntil))
            .ToList();
        var recorderRows = recorders
            .Select(r => new RecorderSyncRow(
                r.RecorderSerial,
                string.Empty,
                (int)(r.Protocol ?? ProtocolType.Ums),
                r.BoundUserNo,
                r.BoundDeptCode,
                r.IsWhitelisted,
                r.IsActive))
            .ToList();

        var published = new List<PublishedConfigItem>();
        var existing = await _changes.GetListAsync(c => c.StationId == stationId);
        var nextVersion = existing.Count == 0 ? 1 : existing.Max(c => c.Version) + 1;
        foreach (var entityType in ConfigDomainPayload.PublishOrder)
        {
            System.Collections.IList rows = entityType switch
            {
                ConfigDomainPayload.EntityTypeDept => deptRows,
                ConfigDomainPayload.EntityTypeUser => userRows,
                ConfigDomainPayload.EntityTypeRole => roleRows,
                ConfigDomainPayload.EntityTypeUserRole => userRoleRows,
                ConfigDomainPayload.EntityTypeAccount => accountRows,
                ConfigDomainPayload.EntityTypeRecorder => recorderRows,
                _ => throw new InvalidOperationException()
            };

            await _changes.InsertAsync(new PlatformConfigChange
            {
                Id = _idGenerator.NextId(),
                StationId = stationId,
                EntityType = entityType,
                Operation = SyncOperation.Upsert,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { rows }),
                Version = nextVersion++,
                PublishedAt = DateTime.Now
            });
            published.Add(new PublishedConfigItem(entityType, nextVersion - 1, rows.Count));
        }

        await _audit.WriteAsync(new AuditLog
        {
            OperatorAccount = User.FindFirst("accountId")?.Value ?? "platform",
            OperationType = "config.publish-domain",
            Target = station.StationCode,
            Detail = $"发布用户域快照：部门{deptRows.Count}/用户{userRows.Count}/角色{roleRows.Count}/账号{accountRows.Count}/记录仪{recorderRows.Count}",
            Result = 1,
            CreatedAt = DateTime.Now
        });

        return Ok(ApiResponse<IReadOnlyList<PublishedConfigItem>>.Ok(published));
    }

    internal static ConfigChangeItem ToItem(PlatformConfigChange c) => new()
    {
        EntityType = c.EntityType,
        Operation = c.Operation,
        PayloadJson = c.PayloadJson,
        Version = c.Version
    };
}

public sealed record PublishConfigRequest(
    string EntityType,
    SyncOperation Operation = SyncOperation.Upsert,
    string PayloadJson = "{}");

public sealed record PublishedConfigItem(string EntityType, long Version, int Rows);
