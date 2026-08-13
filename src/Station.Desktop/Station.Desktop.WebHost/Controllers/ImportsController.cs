using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Station.Application.Audit;
using Station.Application.Recorders;
using Station.Application.Users;
using Station.Domain.Entities;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Repositories;
using AuthService = Station.Application.Authorization.IAuthorizationService;

namespace Station.Desktop.WebHost.Controllers;

/// <summary>单机 Web CSV 导入：部门 / 用户 / 记录仪绑定关系（表头校验、逐行校验、部分成功 + 行号错误报告）。</summary>
[ApiController]
[Authorize]
[Route("api/v1/imports")]
public class ImportsController : ControllerBase
{
    private readonly IUserService _users;
    private readonly IRecorderService _recorders;
    private readonly IRepository<Dept> _depts;
    private readonly IRepository<User> _userRepo;
    private readonly IRepository<Role> _roles;
    private readonly IAuditLogService _audit;
    private readonly AuthService _authorization;

    public ImportsController(
        IUserService users,
        IRecorderService recorders,
        IRepository<Dept> depts,
        IRepository<User> userRepo,
        IRepository<Role> roles,
        IAuditLogService audit,
        AuthService authorization)
    {
        _users = users;
        _recorders = recorders;
        _depts = depts;
        _userRepo = userRepo;
        _roles = roles;
        _audit = audit;
        _authorization = authorization;
    }

    [HttpPost("depts")]
    public async Task<IActionResult> ImportDepts([FromBody] string csv)
    {
        if (!await RequirePermissionAsync(PermissionCodes.DeptManage))
        {
            return StatusCode(403, new { message = "无部门管理权限" });
        }

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

        var errors = new List<ImportErrorWeb>();
        var success = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1;
            var row = rows[i];
            var code = Get(row, header, "编码");
            var name = Get(row, header, "名称");
            var parentCode = Get(row, header, "上级编码");
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new ImportErrorWeb(line, "编码与名称不能为空"));
                continue;
            }

            if (!seen.Add(code) || (await _users.GetDeptTreeAsync()).Any(d => d.Code == code))
            {
                errors.Add(new ImportErrorWeb(line, $"部门编码 {code} 已存在"));
                continue;
            }

            long? parentId = null;
            if (!string.IsNullOrWhiteSpace(parentCode))
            {
                var parent = await _depts.FirstAsync(d => d.Code == parentCode && d.IsActive);
                if (parent is null)
                {
                    errors.Add(new ImportErrorWeb(line, $"上级部门 {parentCode} 不存在或已停用"));
                    continue;
                }

                parentId = parent.Id;
            }

            await _users.CreateDeptAsync(new DeptDto(null, code, name, parentId,
                int.TryParse(Get(row, header, "排序"), out var sort) ? sort : 0));
            success++;
        }

        var result = new ImportResultWeb(rows.Count - 1, success, errors.Count, errors);
        await _audit.WriteAsync(new AuditLog
        {
            OperatorAccount = User.Identity?.Name,
            OperationType = "import.depts",
            Target = $"{result.Success}/{result.Total}",
            Detail = $"部门导入：成功 {result.Success}，失败 {result.Failed}",
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Result = 1
        });
        return Ok(Envelope(result));
    }

    [HttpPost("users")]
    public async Task<IActionResult> ImportUsers([FromBody] string csv)
    {
        if (!await RequirePermissionAsync(PermissionCodes.UserManage))
        {
            return StatusCode(403, new { message = "无用户管理权限" });
        }

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

        var errors = new List<ImportErrorWeb>();
        var success = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < rows.Count; i++)
        {
            var line = i + 1;
            var row = rows[i];
            var userNo = Get(row, header, "工号");
            var name = Get(row, header, "姓名");
            var deptCode = Get(row, header, "部门编码");
            if (string.IsNullOrWhiteSpace(userNo) || string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new ImportErrorWeb(line, "工号与姓名不能为空"));
                continue;
            }

            if (!seen.Add(userNo) || (await _users.GetUsersAsync()).Any(u => u.UserNo == userNo))
            {
                errors.Add(new ImportErrorWeb(line, $"工号 {userNo} 已存在"));
                continue;
            }

            var dept = await _depts.FirstAsync(d => d.Code == deptCode && d.IsActive);
            if (dept is null)
            {
                errors.Add(new ImportErrorWeb(line, $"部门 {deptCode} 不存在或已停用"));
                continue;
            }

            var password = string.IsNullOrWhiteSpace(Get(row, header, "初始密码"))
                ? "Station@123"
                : Get(row, header, "初始密码");
            var created = await _users.CreateUserAsync(new UserDto(null, userNo, name, dept.Id), userNo, password);
            if (!created.Success)
            {
                errors.Add(new ImportErrorWeb(line, created.Message ?? "创建用户失败"));
                continue;
            }

            var roleCodes = Get(row, header, "角色编码")
                .Split([';', '；', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (roleCodes.Length > 0)
            {
                var createdUser = await _userRepo.FirstAsync(u => u.UserNo == userNo);
                var roleIds = new List<long>();
                var roleError = false;
                foreach (var roleCode in roleCodes)
                {
                    var role = await _roles.FirstAsync(r => r.Code == roleCode && r.IsActive);
                    if (role is null)
                    {
                        errors.Add(new ImportErrorWeb(line, $"角色 {roleCode} 不存在或已停用"));
                        roleError = true;
                        break;
                    }

                    roleIds.Add(role.Id);
                }

                if (roleError)
                {
                    continue;
                }

                if (createdUser is not null && roleIds.Count > 0)
                {
                    await _users.AssignRolesAsync(createdUser.Id, roleIds);
                }
            }

            success++;
        }

        var result = new ImportResultWeb(rows.Count - 1, success, errors.Count, errors);
        await _audit.WriteAsync(new AuditLog
        {
            OperatorAccount = User.Identity?.Name,
            OperationType = "import.users",
            Target = $"{result.Success}/{result.Total}",
            Detail = $"用户导入：成功 {result.Success}，失败 {result.Failed}",
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Result = 1
        });
        return Ok(Envelope(result));
    }

    [HttpPost("recorder-bindings")]
    public async Task<IActionResult> ImportRecorderBindings([FromBody] string csv)
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
        if (!header.ContainsKey("序列号") || !header.ContainsKey("用户工号"))
        {
            return BadRequest(new { message = "缺少 序列号/用户工号 列（模板：序列号,用户工号,部门编码）" });
        }

        var errors = new List<ImportErrorWeb>();
        var success = 0;
        foreach (var (row, i) in rows.Skip(1).Select((r, i) => (r, i + 1)))
        {
            var serial = Get(row, header, "序列号");
            var userNo = Get(row, header, "用户工号");
            var deptCode = Get(row, header, "部门编码");
            var recorder = (await _recorders.GetRecordersAsync()).FirstOrDefault(r => r.SerialNumber == serial);
            if (recorder is null)
            {
                errors.Add(new ImportErrorWeb(i + 1, $"记录仪 {serial} 不在台账"));
                continue;
            }

            var user = string.IsNullOrWhiteSpace(userNo) ? null : await _userRepo.FirstAsync(u => u.UserNo == userNo && u.IsActive);
            if (string.IsNullOrWhiteSpace(userNo) || user is null)
            {
                errors.Add(new ImportErrorWeb(i + 1, $"用户 {userNo} 不存在或已停用"));
                continue;
            }

            long? deptId = user.DeptId;
            if (!string.IsNullOrWhiteSpace(deptCode))
            {
                var dept = await _depts.FirstAsync(d => d.Code == deptCode && d.IsActive);
                if (dept is null)
                {
                    errors.Add(new ImportErrorWeb(i + 1, $"部门 {deptCode} 不存在或已停用"));
                    continue;
                }

                deptId = dept.Id;
            }

            await _recorders.UpdateAsync(recorder with { BoundUserId = user.Id, DeptId = deptId });
            success++;
        }

        var result = new ImportResultWeb(rows.Count - 1, success, errors.Count, errors);
        await _audit.WriteAsync(new AuditLog
        {
            OperatorAccount = User.Identity?.Name,
            OperationType = "import.recorder-bindings",
            Target = $"{result.Success}/{result.Total}",
            Detail = $"记录仪绑定导入：成功 {result.Success}，失败 {result.Failed}",
            SourceIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Result = 1
        });
        return Ok(Envelope(result));
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

    private static object Envelope(object data) => new { success = true, code = 0, message = "ok", data };

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

public sealed record ImportErrorWeb(int Line, string Message);

public sealed record ImportResultWeb(int Total, int Success, int Failed, List<ImportErrorWeb> Errors);
