using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Station.Application.Audit;
using Station.Application.Authorization;
using Station.Contracts;
using Station.Contracts.Api;
using Station.Domain.Entities;
using Station.Infrastructure;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;
using AuthService = Station.Application.Authorization.IAuthorizationService;

namespace Station.Platform.Api.Controllers;

/// <summary>
/// 组织/用户 CSV 导入：表头校验、逐行校验、部分成功 + 错误报告。
/// 模板：部门=编码,名称,上级编码,排序；用户=工号,姓名,部门编码,角色编码(分号分隔),初始密码。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/imports")]
public class PlatformImportController : ControllerBase
{
    private readonly IRepository<Dept> _depts;
    private readonly IRepository<User> _users;
    private readonly IRepository<Account> _accounts;
    private readonly IRepository<Role> _roles;
    private readonly IRepository<UserRole> _userRoles;
    private readonly IRepository<PlatformRecorder> _recorders;
    private readonly IRepository<PlatformStation> _stations;
    private readonly IIdGenerator _idGenerator;
    private readonly IPasswordHasher _passwordHasher;
    private readonly AuthService _authorization;
    private readonly IDataScopeProvider _dataScope;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogService _audit;

    public PlatformImportController(
        IRepository<Dept> depts,
        IRepository<User> users,
        IRepository<Account> accounts,
        IRepository<Role> roles,
        IRepository<UserRole> userRoles,
        IRepository<PlatformRecorder> recorders,
        IRepository<PlatformStation> stations,
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
        _userRoles = userRoles;
        _recorders = recorders;
        _stations = stations;
        _idGenerator = idGenerator;
        _passwordHasher = passwordHasher;
        _authorization = authorization;
        _dataScope = dataScope;
        _uow = uow;
        _audit = audit;
    }

    [HttpPost("depts")]
    public async Task<IActionResult> ImportDepts([FromBody] string csv)
    {
        if (!await RequirePermissionAsync(PermissionCodes.DeptManage))
        {
            return StatusCode(403, new { message = "无部门管理权限" });
        }

        var scope = await GetScopeAsync();
        var rows = ParseCsv(csv);
        if (rows.Count < 2)
        {
            return BadRequest(new { message = "CSV 至少需要表头与一行数据" });
        }

        var header = HeaderIndex(rows[0]);
        if (!header.ContainsKey("编码") || !header.ContainsKey("名称"))
        {
            return BadRequest(new { message = "缺少 编码/名称 列（模板：编码,名称,上级编码,排序）" });
        }

        var errors = new List<ImportErrorView>();
        var success = 0;
        var codesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1;
            var row = rows[i];
            var code = Get(row, header, "编码");
            var name = Get(row, header, "名称");
            var parentCode = Get(row, header, "上级编码");
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new ImportErrorView(line, "编码与名称不能为空"));
                continue;
            }

            if (!codesInFile.Add(code) || await _depts.IsAnyAsync(d => d.Code == code))
            {
                errors.Add(new ImportErrorView(line, $"部门编码 {code} 已存在"));
                continue;
            }

            long? parentId = null;
            if (!string.IsNullOrWhiteSpace(parentCode))
            {
                var parent = await _depts.FirstAsync(d => d.Code == parentCode && d.IsActive);
                if (parent is null)
                {
                    errors.Add(new ImportErrorView(line, $"上级部门 {parentCode} 不存在或已停用"));
                    continue;
                }

                if (!scope.IsAll && !scope.AllowedDeptIds.Contains(parent.Id))
                {
                    errors.Add(new ImportErrorView(line, $"无权在 {parentCode} 下创建部门"));
                    continue;
                }

                parentId = parent.Id;
            }
            else if (!scope.IsAll)
            {
                errors.Add(new ImportErrorView(line, "无权创建顶级部门"));
                continue;
            }

            await _depts.InsertAsync(new Dept
            {
                Id = _idGenerator.NextId(),
                Code = code,
                Name = name,
                ParentId = parentId,
                SortOrder = int.TryParse(Get(row, header, "排序"), out var sort) ? sort : 0
            });
            success++;
        }

        await WriteAuditAsync("import.depts", $"{success}/{rows.Count - 1}", $"部门导入：成功 {success}，失败 {errors.Count}");
        return Ok(ApiResponse<ImportResultView>.Ok(
            new ImportResultView(rows.Count - 1, success, errors.Count, errors)));
    }

    [HttpPost("users")]
    public async Task<IActionResult> ImportUsers([FromBody] string csv)
    {
        if (!await RequirePermissionAsync(PermissionCodes.UserManage))
        {
            return StatusCode(403, new { message = "无用户管理权限" });
        }

        var scope = await GetScopeAsync();
        var rows = ParseCsv(csv);
        if (rows.Count < 2)
        {
            return BadRequest(new { message = "CSV 至少需要表头与一行数据" });
        }

        var header = HeaderIndex(rows[0]);
        if (!header.ContainsKey("工号") || !header.ContainsKey("姓名") || !header.ContainsKey("部门编码"))
        {
            return BadRequest(new { message = "缺少 工号/姓名/部门编码 列（模板：工号,姓名,部门编码,角色编码,初始密码）" });
        }

        var errors = new List<ImportErrorView>();
        var success = 0;
        var userNosInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1;
            var row = rows[i];
            var userNo = Get(row, header, "工号");
            var name = Get(row, header, "姓名");
            var deptCode = Get(row, header, "部门编码");
            var password = Get(row, header, "初始密码");
            if (string.IsNullOrWhiteSpace(userNo) || string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new ImportErrorView(line, "工号与姓名不能为空"));
                continue;
            }

            if (!userNosInFile.Add(userNo) ||
                await _users.IsAnyAsync(u => u.UserNo == userNo) ||
                await _accounts.IsAnyAsync(a => a.UserName == userNo))
            {
                errors.Add(new ImportErrorView(line, $"工号 {userNo} 已存在"));
                continue;
            }

            var dept = await _depts.FirstAsync(d => d.Code == deptCode && d.IsActive);
            if (dept is null)
            {
                errors.Add(new ImportErrorView(line, $"部门 {deptCode} 不存在或已停用"));
                continue;
            }

            if (!scope.IsAll && !scope.AllowedDeptIds.Contains(dept.Id))
            {
                errors.Add(new ImportErrorView(line, $"无权在 {deptCode} 下创建用户"));
                continue;
            }

            var roleCodes = Get(row, header, "角色编码")
                .Split([';', '；', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var roleIds = new List<long>();
            var roleError = false;
            foreach (var roleCode in roleCodes)
            {
                var role = await _roles.FirstAsync(r => r.Code == roleCode && r.IsActive);
                if (role is null)
                {
                    errors.Add(new ImportErrorView(line, $"角色 {roleCode} 不存在或已停用"));
                    roleError = true;
                    break;
                }

                roleIds.Add(role.Id);
            }

            if (roleError)
            {
                continue;
            }

            await _uow.UseTranAsync(async () =>
            {
                var users = _uow.GetRepository<User>();
                var accounts = _uow.GetRepository<Account>();
                var userRoles = _uow.GetRepository<UserRole>();
                var userId = _idGenerator.NextId();
                await users.InsertAsync(new User { Id = userId, UserNo = userNo, Name = name, DeptId = dept.Id });
                await accounts.InsertAsync(new Account
                {
                    Id = _idGenerator.NextId(),
                    UserName = userNo,
                    PasswordHash = _passwordHasher.Hash(string.IsNullOrWhiteSpace(password) ? "Station@123" : password),
                    UserId = userId
                });
                foreach (var roleId in roleIds)
                {
                    await userRoles.InsertAsync(new UserRole { Id = _idGenerator.NextId(), UserId = userId, RoleId = roleId });
                }

                return true;
            });
            success++;
        }

        await WriteAuditAsync("import.users", $"{success}/{rows.Count - 1}", $"用户导入：成功 {success}，失败 {errors.Count}");
        return Ok(ApiResponse<ImportResultView>.Ok(
            new ImportResultView(rows.Count - 1, success, errors.Count, errors)));
    }

    /// <summary>记录仪台账导入：序列号,型号,协议,白名单（是/否）。</summary>
    [HttpPost("recorders")]
    public async Task<IActionResult> ImportRecorders([FromBody] string csv)
    {
        if (!await RequirePermissionAsync(PermissionCodes.RecorderManage))
        {
            return StatusCode(403, new { message = "无记录仪管理权限" });
        }

        var rows = ParseCsv(csv);
        if (rows.Count < 2)
        {
            return BadRequest(new { message = "CSV 至少需要表头与一行数据" });
        }

        var header = HeaderIndex(rows[0]);
        if (!header.ContainsKey("序列号"))
        {
            return BadRequest(new { message = "缺少 序列号 列（模板：序列号,型号,协议,白名单）" });
        }

        var errors = new List<ImportErrorView>();
        var success = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1;
            var row = rows[i];
            var serial = Get(row, header, "序列号");
            if (string.IsNullOrWhiteSpace(serial))
            {
                errors.Add(new ImportErrorView(line, "序列号不能为空"));
                continue;
            }

            if (!seen.Add(serial) || await _recorders.IsAnyAsync(r => r.RecorderSerial == serial))
            {
                errors.Add(new ImportErrorView(line, $"序列号 {serial} 已存在"));
                continue;
            }

            var protocolText = Get(row, header, "协议");
            var protocol = protocolText.ToLowerInvariant() switch
            {
                "mtp" or "1" => ProtocolType.Mtp,
                "privatesdk" or "私有sdk" or "2" => ProtocolType.PrivateSdk,
                _ => ProtocolType.Ums
            };
            var whitelist = Get(row, header, "白名单");
            await _recorders.InsertAsync(new PlatformRecorder
            {
                Id = _idGenerator.NextId(),
                RecorderSerial = serial,
                Protocol = protocol,
                FirstSeenAt = DateTime.Now,
                LastSeenAt = DateTime.Now,
                IsWhitelisted = whitelist == "是" || whitelist == "true" || whitelist == "1",
                IsActive = true,
                UpdatedAt = DateTime.Now
            });
            success++;
        }

        var result = new ImportResultView(rows.Count - 1, success, errors.Count, errors);
        await WriteAuditAsync("import.recorders", $"{result.Success}/{result.Total}",
            $"记录仪导入：成功 {result.Success}，失败 {result.Failed}");
        return Ok(ApiResponse<ImportResultView>.Ok(result));
    }

    /// <summary>采集站台账导入：站编号,系统,架构,版本,部门编码。</summary>
    [HttpPost("stations")]
    public async Task<IActionResult> ImportStations([FromBody] string csv)
    {
        if (!await RequirePermissionAsync(PermissionCodes.StationManage))
        {
            return StatusCode(403, new { message = "无采集站管理权限" });
        }

        var rows = ParseCsv(csv);
        if (rows.Count < 2)
        {
            return BadRequest(new { message = "CSV 至少需要表头与一行数据" });
        }

        var header = HeaderIndex(rows[0]);
        if (!header.ContainsKey("站编号"))
        {
            return BadRequest(new { message = "缺少 站编号 列（模板：站编号,系统,架构,版本,部门编码）" });
        }

        var errors = new List<ImportErrorView>();
        var success = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1;
            var row = rows[i];
            var code = Get(row, header, "站编号");
            if (string.IsNullOrWhiteSpace(code))
            {
                errors.Add(new ImportErrorView(line, "站编号不能为空"));
                continue;
            }

            if (!seen.Add(code) || await _stations.IsAnyAsync(s => s.StationCode == code))
            {
                errors.Add(new ImportErrorView(line, $"站编号 {code} 已存在"));
                continue;
            }

            long? deptId = null;
            var deptCode = Get(row, header, "部门编码");
            if (!string.IsNullOrWhiteSpace(deptCode))
            {
                var dept = await _depts.FirstAsync(d => d.Code == deptCode && d.IsActive);
                if (dept is null)
                {
                    errors.Add(new ImportErrorView(line, $"部门 {deptCode} 不存在或已停用"));
                    continue;
                }

                deptId = dept.Id;
            }

            await _stations.InsertAsync(new PlatformStation
            {
                Id = _idGenerator.NextId(),
                StationCode = code,
                OsVersion = Get(row, header, "系统"),
                CpuArch = Get(row, header, "架构"),
                SoftwareVersion = Get(row, header, "版本"),
                DeptId = deptId,
                RegisteredAt = DateTime.Now
            });
            success++;
        }

        var result = new ImportResultView(rows.Count - 1, success, errors.Count, errors);
        await WriteAuditAsync("import.stations", $"{result.Success}/{result.Total}",
            $"采集站导入：成功 {result.Success}，失败 {result.Failed}");
        return Ok(ApiResponse<ImportResultView>.Ok(result));
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

    private async Task WriteAuditAsync(string operationType, string? target, string? detail)
    {
        await _audit.WriteAsync(new AuditLog
        {
            OperatorAccount = User.Identity?.Name,
            OperationType = operationType,
            Target = target,
            Detail = detail,
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Result = 1
        });
    }

    private Task<DataScopeResult> GetScopeAsync() =>
        DataScopeHelper.GetScopeAsync(User, _authorization, _dataScope);

    private static List<string[]> ParseCsv(string csv)
    {
        return csv.Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim('\uFEFF').Trim())
            .Where(l => l.Length > 0)
            .Select(l => l.Split(',').Select(c => c.Trim().Trim('"')).ToArray())
            .ToList();
    }

    private static Dictionary<string, int> HeaderIndex(string[] header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
        {
            map[header[i].Trim()] = i;
        }

        return map;
    }

    private static string Get(string[] row, Dictionary<string, int> header, string column) =>
        header.TryGetValue(column, out var index) && index < row.Length ? row[index] : string.Empty;
}

public sealed record ImportErrorView(int Line, string Message);

public sealed record ImportResultView(int Total, int Success, int Failed, List<ImportErrorView> Errors);
