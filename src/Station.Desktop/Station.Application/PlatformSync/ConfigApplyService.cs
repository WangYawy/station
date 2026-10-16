using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Station.Application.Auditing;
using Station.Application.Collecting;
using Station.Contracts;
using Station.Contracts.Sync;
using Station.Data.IdGeneration;
using Station.Domain.Accounts;
using Station.Domain.Auditing;
using Station.Domain.Departments;
using Station.Domain.Permissions;
using Station.Domain.Recorders;
using Station.Domain.Roles;
using Station.Domain.Users;

namespace Station.Application.PlatformSync;

public sealed class ConfigApplyService : IConfigApplyService
{
    private readonly CollectOptions _collectOptions;
    //private readonly StorageOptions _storageOptions;
    private readonly IConfigSyncState _state;
    private readonly IAuditLogService _audit;

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IIdGenerator _idGenerator;
    private readonly ILogger<ConfigApplyService> _logger;

    public ConfigApplyService(
        CollectOptions collectOptions,
        IConfigSyncState state,
        IAuditLogService audit,
        IIdGenerator idGenerator,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<ConfigApplyService> logger)
    {
        _collectOptions = collectOptions;
        //_storageOptions = storageOptions;
        _state = state;
        _audit = audit;
        _idGenerator = idGenerator;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task<int> ApplyAsync(ConfigSyncResponse response)
    {
        var applied = 0;
        foreach (var change in response.Changes)
        {
            string detail;
            try
            {
                detail = change.EntityType switch
                {
                    "CollectPolicy" => ApplyCollectPolicy(change),
                    "StoragePolicy" => ApplyStoragePolicy(change),
                    ConfigDomainPayload.EntityTypeDept => await ApplyDeptAsync(change),
                    ConfigDomainPayload.EntityTypeUser => await ApplyUserAsync(change),
                    ConfigDomainPayload.EntityTypeRole => await ApplyRoleAsync(change),
                    ConfigDomainPayload.EntityTypeUserRole => await ApplyUserRoleAsync(change),
                    ConfigDomainPayload.EntityTypeAccount => await ApplyAccountAsync(change),
                    ConfigDomainPayload.EntityTypeRecorder => await ApplyRecorderAsync(change),
                    _ => $"暂不支持配置类型 {change.EntityType}，已跳过"
                };

                _state.RecordApplied(change.EntityType, change.Version);
                applied++;
                _logger.LogInformation("配置应用成功：{Type} v{Version}：{Detail}", change.EntityType, change.Version, detail);
                await _audit.WriteAsync(new AuditLog
                {
                    OperatorAccount = "platform",
                    OperationType = "config-apply",
                    Target = change.EntityType,
                    Detail = $"v{change.Version}：{detail}",
                    Result = 1,
                    CreatedAt = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "配置应用失败：{Type} v{Version}", change.EntityType, change.Version);
                // 单条失败不阻塞后续变更：记失败审计、不记录已应用版本，下轮轮询自动重试
                await _audit.WriteAsync(new AuditLog
                {
                    OperatorAccount = "platform",
                    OperationType = "config-apply",
                    Target = change.EntityType,
                    Detail = $"v{change.Version}：应用失败：{ex.Message}",
                    Result = 0,
                    CreatedAt = DateTime.Now
                });
            }
        }

        return applied;
    }

    private string ApplyCollectPolicy(ConfigChangeItem change)
    {
        using var json = JsonDocument.Parse(change.PayloadJson);
        var root = json.RootElement;
        if (root.TryGetProperty("autoCollectOnConnect", out var auto) && auto.ValueKind == JsonValueKind.True)
        {
            _collectOptions.AutoCollectOnConnect = true;
        }
        else if (root.TryGetProperty("autoCollectOnConnect", out var autoFalse) && autoFalse.ValueKind == JsonValueKind.False)
        {
            _collectOptions.AutoCollectOnConnect = false;
        }

        if (root.TryGetProperty("eraseAfterComplete", out var erase))
        {
            _collectOptions.EraseAfterComplete = erase.ValueKind == JsonValueKind.True;
        }

        if (root.TryGetProperty("skipCollected", out var skip))
        {
            _collectOptions.SkipCollected = skip.ValueKind == JsonValueKind.True;
        }

        if (root.TryGetProperty("maxEmergencyTasks", out var emergency) && emergency.TryGetInt32(out var maxEmergency))
        {
            _collectOptions.MaxEmergencyTasks = Math.Max(0, maxEmergency);
        }

        return $"采集策略已热更新（AutoCollect={_collectOptions.AutoCollectOnConnect}, Erase={_collectOptions.EraseAfterComplete}, Skip={_collectOptions.SkipCollected}, 紧急上限={_collectOptions.MaxEmergencyTasks}）";
    }

    private string ApplyStoragePolicy(ConfigChangeItem change)
    {
        //using var json = JsonDocument.Parse(change.PayloadJson);
        //var root = json.RootElement;
        //if (root.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String)
        //{
        //    _storageOptions.Target = Enum.TryParse<StorageTargetKind>(target.GetString(), true, out var kind)
        //        ? kind
        //        : _storageOptions.Target;
        //}

        //if (root.TryGetProperty("directoryTemplate", out var template) && template.ValueKind == JsonValueKind.String)
        //{
        //    _storageOptions.DirectoryTemplate = template.GetString()!;
        //}

        //if (root.TryGetProperty("retryCount", out var retry) && retry.TryGetInt32(out var retryValue))
        //{
        //    _storageOptions.RetryCount = retryValue;
        //}

        //if (root.TryGetProperty("circuitBreakerThreshold", out var breaker) && breaker.TryGetInt32(out var breakerValue))
        //{
        //    _storageOptions.CircuitBreakerThreshold = breakerValue;
        //}

        //return $"存储策略已热更新（Target={_storageOptions.Target}, Retry={_storageOptions.RetryCount}, 熔断阈值={_storageOptions.CircuitBreakerThreshold}）";
        return "";
    }

    // ==================== 用户域（组织/用户/角色/账号/记录仪） ====================

    /// <summary>创建独立客户端，规避共享作用域与采集/查询并发时的连接竞争。</summary>
    private IServiceScope NewScope() =>
        _serviceScopeFactory.CreateScope();

    private static List<T> DeserializeRows<T>(string payloadJson)
    {
        using var json = JsonDocument.Parse(payloadJson);
        if (!json.RootElement.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return rows.EnumerateArray().Select(r => r.Deserialize<T>()!).ToList();
    }

    private async Task<string> ApplyDeptAsync(ConfigChangeItem change)
    {
        var rows = DeserializeRows<DeptSyncRow>(change.PayloadJson);
        using var scope = NewScope();
        var deptRepo = scope.ServiceProvider.GetRequiredService<IDeptSyncRepository>();

        var existing = await deptRepo.GetListAsync();
        var byCode = existing.ToDictionary(d => d.Code, d => d, StringComparer.OrdinalIgnoreCase);
        var toAdd = new List<Dept>();
        var toUpdate = new List<Dept>();
        var now = DateTime.Now;

        foreach (var row in rows)
        {
            if (byCode.TryGetValue(row.Code, out var dept))
            {
                dept.UpdateInfo(row.Name, row.SortOrder, now);
                if (row.IsActive) dept.Activate(now); else dept.Deactivate(now);
                toUpdate.Add(dept);
            }
            else
            {
                var created = Dept.Create(
                    _idGenerator.NewId(), row.Code, row.Name, null, row.SortOrder, now);
                if (!row.IsActive) created.Deactivate(now);
                byCode[row.Code] = created;
                toAdd.Add(created);
            }
        }

        // 第二遍回填父级
        foreach (var row in rows)
        {
            if (!byCode.TryGetValue(row.Code, out var dept)) continue;
            var parentId = row.ParentCode is { } pc && byCode.TryGetValue(pc, out var parent)
                ? parent.Id
                : (long?)null;
            dept.MoveTo(parentId, now);
            if (!toAdd.Contains(dept) && !toUpdate.Contains(dept)) toUpdate.Add(dept);
        }

        var seen = rows.Select(r => r.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var d in existing.Where(d => !seen.Contains(d.Code) && d.IsActive))
        {
            d.Deactivate(now);
            toUpdate.Add(d);
        }

        foreach (var d in toAdd) await deptRepo.InsertAsync(d);
        if (toUpdate.Count > 0) await deptRepo.UpdateRangeAsync(toUpdate);

        return $"部门快照已应用：{rows.Count} 条";
    }

    private async Task<string> ApplyUserAsync(ConfigChangeItem change)
    {
        var rows = DeserializeRows<UserSyncRow>(change.PayloadJson);
        using var scope = NewScope();

        var userRepo = scope.ServiceProvider.GetRequiredService<IUserSyncRepository>();
        var deptRepo = scope.ServiceProvider.GetRequiredService<IDeptSyncRepository>();

        var existing = await userRepo.GetListAsync();
        var byUserNo = existing.ToDictionary(u => u.UserNo, u => u, StringComparer.OrdinalIgnoreCase);

        var depts = await deptRepo.GetListAsync();
        var deptIdByCode = depts.ToDictionary(d => d.Code, d => d.Id, StringComparer.OrdinalIgnoreCase);
        var fallbackDeptId = deptIdByCode.TryGetValue("ROOT", out var rootId)
            ? rootId
            : depts.Count > 0 ? depts[0].Id : 0;

        var now = DateTime.Now;
        var toAdd = new List<User>();
        var toUpdate = new List<User>();

        foreach (var row in rows)
        {
            var deptId = deptIdByCode.TryGetValue(row.DeptCode, out var did) ? did : fallbackDeptId;

            if (byUserNo.TryGetValue(row.UserNo, out var user))
            {
                user.UpdateBasicInfo(row.UserNo, row.Name, now);
                if (user.DeptId != deptId) user.ChangeDept(deptId, now);
                if (row.IsActive) user.Activate(now); else user.Deactivate(now);
                toUpdate.Add(user);
            }
            else
            {
                var created = User.Create(_idGenerator.NewId(), row.UserNo, row.Name, deptId, now);
                if (!row.IsActive) created.Deactivate(now);
                byUserNo[row.UserNo] = created;
                toAdd.Add(created);
            }
        }

        var seen = rows.Select(r => r.UserNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var u in existing.Where(u => !seen.Contains(u.UserNo) && u.IsActive))
        {
            u.Deactivate(now);
            toUpdate.Add(u);
        }

        foreach (var u in toAdd) await userRepo.InsertAsync(u);
        if (toUpdate.Count > 0) await userRepo.UpdateRangeAsync(toUpdate);

        return $"用户快照已应用：{rows.Count} 条";
    }

    private async Task<string> ApplyRoleAsync(ConfigChangeItem change)
    {
        var rows = DeserializeRows<RoleSyncRow>(change.PayloadJson);
        using var scope = NewScope();

        var roleRepo = scope.ServiceProvider.GetRequiredService<IRoleSyncRepository>();
        var permissionRepo = scope.ServiceProvider.GetRequiredService<IPermissionRepository>();
        var rolePermissionRepo = scope.ServiceProvider.GetRequiredService<IRolePermissionRepository>();

        var existing = await roleRepo.GetListAsync();
        var byCode = existing.ToDictionary(r => r.Code, r => r, StringComparer.OrdinalIgnoreCase);
        var permissionIdByCode = (await permissionRepo.GetListAsync())
            .ToDictionary(p => p.Code, p => p.Id, StringComparer.OrdinalIgnoreCase);

        var now = DateTime.Now;
        var toAdd = new List<Role>();
        var toUpdate = new List<Role>();

        foreach (var row in rows)
        {
            Role role;
            if (byCode.TryGetValue(row.Code, out var existingRole))
            {
                role = existingRole;
                role.Rename(row.Name, now);
                role.ChangeDataScope((DataScope)row.DataScope, now);
                if (row.IsActive) role.Activate(now); else role.Deactivate(now);
                toUpdate.Add(role);
            }
            else
            {
                role = Role.Create(
                    _idGenerator.NewId(), row.Code, row.Name,
                    (DataScope)row.DataScope, row.IsSystem, now);
                if (!row.IsActive) role.Deactivate(now);
                byCode[row.Code] = role;
                toAdd.Add(role);
            }

            // 角色权限全量重建
            await rolePermissionRepo.DeleteByRoleIdAsync(role.Id);
            var permissionIds = row.PermissionCodes
                .Where(code => permissionIdByCode.ContainsKey(code))
                .Select(code => permissionIdByCode[code])
                .ToList();
            await rolePermissionRepo.AddRangeAsync(role.Id, permissionIds);
        }

        var seen = rows.Select(r => r.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var r in existing.Where(r => !seen.Contains(r.Code) && r.IsActive))
        {
            r.Deactivate(now);
            toUpdate.Add(r);
        }

        foreach (var r in toAdd) await roleRepo.InsertAsync(r);
        if (toUpdate.Count > 0) await roleRepo.UpdateRangeAsync(toUpdate);

        return $"角色快照已应用：{rows.Count} 条";
    }

    private async Task<string> ApplyUserRoleAsync(ConfigChangeItem change)
    {
        var rows = DeserializeRows<UserRoleSyncRow>(change.PayloadJson);
        using var scope = NewScope();

        var userRepo = scope.ServiceProvider.GetRequiredService<IUserSyncRepository>();
        var roleRepo = scope.ServiceProvider.GetRequiredService<IRoleSyncRepository>();
        var userRoleRepo = scope.ServiceProvider.GetRequiredService<IUserRoleRepository>();

        var userNoToId = (await userRepo.GetListAsync())
            .ToDictionary(u => u.UserNo, u => u.Id, StringComparer.OrdinalIgnoreCase);
        var roleCodeToId = (await roleRepo.GetListAsync())
            .ToDictionary(r => r.Code, r => r.Id, StringComparer.OrdinalIgnoreCase);

        await userRoleRepo.DeleteAllAsync();

        var links = rows
            .Where(r => userNoToId.ContainsKey(r.UserNo) && roleCodeToId.ContainsKey(r.RoleCode))
            .Select(r => (userNoToId[r.UserNo], roleCodeToId[r.RoleCode]))
            .ToList();

        await userRoleRepo.AddRangeBatchAsync(links);

        return $"用户角色快照已应用：{rows.Count} 条（有效 {links.Count} 条）";
    }

    private async Task<string> ApplyAccountAsync(ConfigChangeItem change)
    {
        var rows = DeserializeRows<AccountSyncRow>(change.PayloadJson);
        using var scope = NewScope();
        var accountRepo = scope.ServiceProvider.GetRequiredService<IAccountLoopRepository>();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserSyncRepository>();
        var existing = await accountRepo.GetListAsync();
        var idByUserName = existing.ToDictionary(a => a.UserName, a => a.Id, StringComparer.OrdinalIgnoreCase);
        var userNoToId = (await userRepo.GetListAsync())
            .ToDictionary(u => u.UserNo, u => u.Id, StringComparer.OrdinalIgnoreCase);
        var touched = new List<Account>();

        var now = DateTime.Now;
        var toAdd = new List<Account>();
        var toUpdate = new List<Account>();

        foreach (var row in rows)
        {
            var userId = row.UserNo is { } userNo && userNoToId.TryGetValue(userNo, out var uid)
                ? uid
                : (long?)null;
            if (idByUserName.TryGetValue(row.UserName, out var id))
            {
                var account = existing.First(a => a.Id == id);
                // 平台权威覆盖：密码、绑定、启用状态
                account.ChangePassword(row.PasswordHash, now);
                account.BindUser(userId, now);
                if (row.IsEnabled) account.Enable(now);
                else account.Disable(now);
                // 本地运行态（失败次数/锁定截止）不随平台快照覆盖
                toUpdate.Add(account);
            }
            else
            {
                var created = Account.Create(
                   _idGenerator.NewId(),
                   row.UserName,
                   row.PasswordHash,
                   userId,
                   now);

                if (!row.IsEnabled)
                {
                    created.Disable(now);
                }

                toAdd.Add(created);
            }
        }

        // 快照外缺失账号禁用（保留账号，不软删除）
        var seen = rows.Select(r => r.UserName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var account in existing.Where(a => !seen.Contains(a.UserName) && a.IsEnabled))
        {
            account.Disable(now);
            toUpdate.Add(account);
        }

        if (toUpdate.Count > 0)
        {
            await accountRepo.UpdateRangeAsync(touched);
        }

        return $"账号快照已应用：{rows.Count} 条";
    }

    private async Task<string> ApplyRecorderAsync(ConfigChangeItem change)
    {
        var rows = DeserializeRows<RecorderSyncRow>(change.PayloadJson);
        using var scope = NewScope();

        var recorderRepo = scope.ServiceProvider.GetRequiredService<IRecorderSyncRepository>();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserSyncRepository>();
        var deptRepo = scope.ServiceProvider.GetRequiredService<IDeptSyncRepository>();

        var existing = recorderRepo.GetList();
        var bySerial = existing.ToDictionary(r => r.SerialNumber, r => r, StringComparer.OrdinalIgnoreCase);

        var userNoToId = (await userRepo.GetListAsync())
            .ToDictionary(u => u.UserNo, u => u.Id, StringComparer.OrdinalIgnoreCase);
        var deptIdByCode = (await deptRepo.GetListAsync())
            .ToDictionary(d => d.Code, d => d.Id, StringComparer.OrdinalIgnoreCase);

        var now = DateTime.Now;
        var toAdd = new List<Recorder>();
        var toUpdate = new List<Recorder>();

        foreach (var row in rows)
        {
            var boundUserId = row.BoundUserNo is { } un && userNoToId.TryGetValue(un, out var uid)
                ? uid : (long?)null;
            var deptId = row.DeptCode is { } dc && deptIdByCode.TryGetValue(dc, out var did)
                ? did : (long?)null;

            if (bySerial.TryGetValue(row.SerialNumber, out var existingRecorder))
            {
                existingRecorder.ChangeModel(row.Model, now);
                existingRecorder.ChangeProtocol((ProtocolType)row.Protocol, now);
                existingRecorder.BindTo(boundUserId, deptId, now);
                if (row.IsAuthorized) existingRecorder.Authorize(now);
                else existingRecorder.RevokeAuthorization(now);
                if (row.IsActive) existingRecorder.Activate(now);
                else existingRecorder.Deactivate(now);
                toUpdate.Add(existingRecorder);
            }
            else
            {
                var created = Recorder.Create(
                    _idGenerator.NewId(), row.SerialNumber, row.SerialNumber, row.SerialNumber, row.Model,
                    (ProtocolType)row.Protocol, row.IsAuthorized, now);
                created.BindTo(boundUserId, deptId, now);
                if (!row.IsActive) created.Deactivate(now);
                bySerial[row.SerialNumber] = created;
                toAdd.Add(created);
            }
        }

        // 快照缺失 → 停用
        var seen = rows.Select(r => r.SerialNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var r in existing.Where(r => !seen.Contains(r.SerialNumber) && r.IsActive))
        {
            r.Deactivate(now);
            toUpdate.Add(r);
        }

        foreach (var r in toAdd) recorderRepo.Insert(r);
        if (toUpdate.Count > 0) recorderRepo.UpdateRange(toUpdate);

        return $"记录仪白名单快照已应用：{rows.Count} 条";
    }
}
