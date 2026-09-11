using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Station.Application.Audit;
using Station.Application.Collecting;
using Station.Application.IdGenerators;
using Station.Contracts;
using Station.Contracts.Sync;
using Station.Domain.Entities;
using Station.Domain.Enums;
using Station.Domain.Repositories;
using static System.Formats.Asn1.AsnWriter;

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
        var deptRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<Dept>>();
        var existing = await deptRepo.GetListAsync();
        var idByCode = existing.ToDictionary(d => d.Code, d => d.Id, StringComparer.OrdinalIgnoreCase);
        var entityById = existing.ToDictionary(d => d.Id, d => d);
        var toUpdate = new List<Dept>();

        foreach (var row in rows)
        {
            if (idByCode.TryGetValue(row.Code, out var id))
            {
                var dept = existing.First(d => d.Id == id);
                dept.Name = row.Name;
                dept.SortOrder = row.SortOrder;
                dept.IsActive = row.IsActive;
                toUpdate.Add(dept);
            }
            else
            {
                var dept = new Dept
                {
                    Id = _idGenerator.NextId(),
                    Code = row.Code,
                    Name = row.Name,
                    SortOrder = row.SortOrder,
                    IsActive = row.IsActive
                };
                idByCode[row.Code] = dept.Id;
                entityById[dept.Id] = dept;
                await deptRepo.InsertAsync(dept);
                toUpdate.Add(dept); // 第二遍回填父级后统一落库
            }
        }

        // 第二遍回填父级（避免行序导致父级未落库）
        foreach (var row in rows)
        {
            if (!idByCode.TryGetValue(row.Code, out var id) || !entityById.TryGetValue(id, out var dept))
            {
                continue;
            }

            dept.ParentId = row.ParentCode is { } parentCode &&
                            idByCode.TryGetValue(parentCode, out var parentId)
                ? parentId
                : null;
        }

        // 快照外缺失部门软停用（保留历史引用，不物理删除）
        var seen = rows.Select(r => r.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var disabled = existing.Count(d => !seen.Contains(d.Code) && d.IsActive);
        foreach (var dept in existing.Where(d => !seen.Contains(d.Code) && d.IsActive))
        {
            dept.IsActive = false;
            toUpdate.Add(dept);
        }

        if (toUpdate.Count > 0)
        {
            await deptRepo.UpdateRangeAsync(toUpdate);
        }

        return $"部门快照已应用：{rows.Count} 条（软停用 {disabled} 条）";
    }

    private async Task<string> ApplyUserAsync(ConfigChangeItem change)
    {
        var rows = DeserializeRows<UserSyncRow>(change.PayloadJson);
        using var scope = NewScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<User>>();
        var deptRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<Dept>>();
        var existing = await userRepo.GetListAsync();
        var idByUserNo = existing.ToDictionary(u => u.UserNo, u => u.Id, StringComparer.OrdinalIgnoreCase);
        var depts = await deptRepo.GetListAsync();
        var deptIdByCode = depts.ToDictionary(d => d.Code, d => d.Id, StringComparer.OrdinalIgnoreCase);
        var fallbackDeptId = deptIdByCode.TryGetValue("ROOT", out var rootId)
            ? rootId
            : depts.Count > 0 ? depts[0].Id : 0;
        var touched = new List<User>();

        foreach (var row in rows)
        {
            var deptId = deptIdByCode.TryGetValue(row.DeptCode, out var did) ? did : fallbackDeptId;
            if (idByUserNo.TryGetValue(row.UserNo, out var id))
            {
                var user = existing.First(u => u.Id == id);
                user.Name = row.Name;
                user.DeptId = deptId;
                user.IsActive = row.IsActive;
                touched.Add(user);
            }
            else
            {
                var user = new User
                {
                    Id = _idGenerator.NextId(),
                    UserNo = row.UserNo,
                    Name = row.Name,
                    DeptId = deptId,
                    IsActive = row.IsActive
                };
                idByUserNo[row.UserNo] = user.Id;
                await userRepo.InsertAsync(user);
            }
        }

        var seen = rows.Select(r => r.UserNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var user in existing.Where(u => !seen.Contains(u.UserNo) && u.IsActive))
        {
            user.IsActive = false;
            touched.Add(user);
        }

        if (touched.Count > 0)
        {
            await userRepo.UpdateRangeAsync(touched);
        }

        return $"用户快照已应用：{rows.Count} 条";
    }

    private async Task<string> ApplyRoleAsync(ConfigChangeItem change)
    {
        var rows = DeserializeRows<RoleSyncRow>(change.PayloadJson);
        using var scope = NewScope();
        var roleRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<Role>>();
        var permissionRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<Permission>>();
        var rolePermissionRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<RolePermission>>();
        var existing = await roleRepo.GetListAsync();
        var idByCode = existing.ToDictionary(r => r.Code, r => r.Id, StringComparer.OrdinalIgnoreCase);
        var permissionIdByCode = (await permissionRepo.GetListAsync())
            .ToDictionary(p => p.Code, p => p.Id, StringComparer.OrdinalIgnoreCase);
        var touched = new List<Role>();

        foreach (var row in rows)
        {
            Role role;
            if (idByCode.TryGetValue(row.Code, out var id))
            {
                role = existing.First(r => r.Id == id);
                role.Name = row.Name;
                role.DataScope = (DataScope)row.DataScope;
                role.IsActive = row.IsActive;
                touched.Add(role);
            }
            else
            {
                role = new Role
                {
                    Id = _idGenerator.NextId(),
                    Code = row.Code,
                    Name = row.Name,
                    DataScope = (DataScope)row.DataScope,
                    IsSystem = row.IsSystem,
                    IsActive = row.IsActive
                };
                idByCode[row.Code] = role.Id;
                await roleRepo.InsertAsync(role);
            }

            // 角色权限全量重建（幂等）
            await rolePermissionRepo.DeleteAsync(rp => rp.RoleId == role.Id);

            var links = row.PermissionCodes
                .Where(code => permissionIdByCode.TryGetValue(code, out var _))
                .Select(code => new RolePermission
                {
                    Id = _idGenerator.NextId(),
                    RoleId = role.Id,
                    PermissionId = permissionIdByCode[code]
                })
                .ToList();
            if (links.Count > 0)
            {
                await rolePermissionRepo.InsertRangeAsync(links);
            }
        }

        var seen = rows.Select(r => r.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var role in existing.Where(r => !seen.Contains(r.Code) && r.IsActive))
        {
            role.IsActive = false;
            touched.Add(role);
        }

        if (touched.Count > 0)
        {
            await roleRepo.UpdateRangeAsync(touched);
        }

        return $"角色快照已应用：{rows.Count} 条";
    }

    private async Task<string> ApplyUserRoleAsync(ConfigChangeItem change)
    {
        var rows = DeserializeRows<UserRoleSyncRow>(change.PayloadJson);
        using var scope = NewScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<User>>();
        var roleRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<Role>>();
        var userRoleRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<UserRole>>();
        var userNoToId = (await userRepo.GetListAsync())
            .ToDictionary(u => u.UserNo, u => u.Id, StringComparer.OrdinalIgnoreCase);
        var roleCodeToId = (await roleRepo.GetListAsync())
            .ToDictionary(r => r.Code, r => r.Id, StringComparer.OrdinalIgnoreCase);

        // 派生表全量重建：先清空再按快照插入
        await userRoleRepo.DeleteAsync(t => 1 == 1);
        var links = rows
            .Where(r => userNoToId.TryGetValue(r.UserNo, out var _) &&
                        roleCodeToId.TryGetValue(r.RoleCode, out var _))
            .Select(r => new UserRole
            {
                Id = _idGenerator.NextId(),
                UserId = userNoToId[r.UserNo],
                RoleId = roleCodeToId[r.RoleCode]
            })
            .ToList();
        if (links.Count > 0)
        {
            await  userRoleRepo.InsertRangeAsync(links);
        }

        return $"用户角色快照已应用：{rows.Count} 条（有效 {links.Count} 条）";
    }

    private async Task<string> ApplyAccountAsync(ConfigChangeItem change)
    {
        var rows = DeserializeRows<AccountSyncRow>(change.PayloadJson);
        using var scope = NewScope();
        var accountRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<Account>>();
        var userRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<User>>();
        var existing = await accountRepo.GetListAsync();
        var idByUserName = existing.ToDictionary(a => a.UserName, a => a.Id, StringComparer.OrdinalIgnoreCase);
        var userNoToId = (await userRepo.GetListAsync())
            .ToDictionary(u => u.UserNo, u => u.Id, StringComparer.OrdinalIgnoreCase);
        var touched = new List<Account>();

        foreach (var row in rows)
        {
            var userId = row.UserNo is { } userNo && userNoToId.TryGetValue(userNo, out var uid)
                ? uid
                : (long?)null;
            if (idByUserName.TryGetValue(row.UserName, out var id))
            {
                var account = existing.First(a => a.Id == id);
                account.PasswordHash = row.PasswordHash;
                account.UserId = userId;
                account.IsEnabled = row.IsEnabled;
                // 本地运行态（失败次数/锁定截止）不随平台快照覆盖
                touched.Add(account);
            }
            else
            {
                await accountRepo.InsertAsync(new Account
                {
                    Id = _idGenerator.NextId(),
                    UserName = row.UserName,
                    PasswordHash = row.PasswordHash,
                    UserId = userId,
                    IsEnabled = row.IsEnabled,
                    FailedLoginAttempts = row.FailedLoginAttempts,
                    LockedUntil = row.LockedUntil
                });
            }
        }

        var seen = rows.Select(r => r.UserName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var account in existing.Where(a => !seen.Contains(a.UserName) && a.IsEnabled))
        {
            account.IsEnabled = false;
            touched.Add(account);
        }

        if (touched.Count > 0)
        {
            await accountRepo.UpdateRangeAsync(touched);
        }

        return $"账号快照已应用：{rows.Count} 条";
    }

    private async Task<string> ApplyRecorderAsync(ConfigChangeItem change)
    {
        var rows = DeserializeRows<RecorderSyncRow>(change.PayloadJson);
        using var scope = NewScope();
        var recorderRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<Recorder>>();
        var userRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<User>>();
        var deptRepo = scope.ServiceProvider.GetRequiredService<ILoopRepository<Dept>>();
        var existing = await recorderRepo.GetListAsync();
        var idBySerial = existing.ToDictionary(r => r.SerialNumber, r => r.Id, StringComparer.OrdinalIgnoreCase);
        var userNoToId = (await userRepo.GetListAsync())
            .ToDictionary(u => u.UserNo, u => u.Id, StringComparer.OrdinalIgnoreCase);
        var deptIdByCode = (await deptRepo.GetListAsync())
            .ToDictionary(d => d.Code, d => d.Id, StringComparer.OrdinalIgnoreCase);
        var touched = new List<Recorder>();

        foreach (var row in rows)
        {
            var boundUserId = row.BoundUserNo is { } userNo && userNoToId.TryGetValue(userNo, out var uid)
                ? uid
                : (long?)null;
            var deptId = row.DeptCode is { } deptCode && deptIdByCode.TryGetValue(deptCode, out var did)
                ? did
                : (long?)null;
            if (idBySerial.TryGetValue(row.SerialNumber, out var id))
            {
                var recorder = existing.First(r => r.Id == id);
                recorder.Model = row.Model;
                recorder.Protocol = (ProtocolType)row.Protocol;
                recorder.BoundUserId = boundUserId;
                recorder.DeptId = deptId;
                recorder.IsAuthorized = row.IsAuthorized;
                recorder.IsActive = row.IsActive;
                touched.Add(recorder);
            }
            else
            {
                await recorderRepo.InsertAsync(new Recorder
                {
                    Id = _idGenerator.NextId(),
                    SerialNumber = row.SerialNumber,
                    Model = row.Model,
                    Protocol = (ProtocolType)row.Protocol,
                    BoundUserId = boundUserId,
                    DeptId = deptId,
                    IsAuthorized = row.IsAuthorized,
                    IsActive = row.IsActive
                });
            }
        }

        var seen = rows.Select(r => r.SerialNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var recorder in existing.Where(r => !seen.Contains(r.SerialNumber) && r.IsActive))
        {
            recorder.IsActive = false;
            touched.Add(recorder);
        }

        if (touched.Count > 0)
        {
            await recorderRepo.UpdateRangeAsync(touched);
        }

        return $"记录仪白名单快照已应用：{rows.Count} 条";
    }
}
