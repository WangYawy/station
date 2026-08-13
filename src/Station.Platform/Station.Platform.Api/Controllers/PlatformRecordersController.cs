using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Station.Application.Audit;
using Station.Application.Authorization;
using Station.Contracts;
using Station.Contracts.Api;
using Station.Contracts.Commands;
using Station.Domain.Entities;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Persistence;
using Station.Infrastructure.Repositories;
using Station.Platform.Domain.Entities;
using Station.Platform.Api.Realtime;
using AuthService = Station.Application.Authorization.IAuthorizationService;

namespace Station.Platform.Api.Controllers;

/// <summary>平台记录仪台账：列表（按部门数据范围）、白名单、绑定（下发 WriteBinding 指令）。</summary>
[ApiController]
[Authorize]
[Route("api/v1/recorders")]
public class PlatformRecordersController : ControllerBase
{
    private readonly IRepository<PlatformRecorder> _recorders;
    private readonly IRepository<PlatformFileMetadata> _files;
    private readonly IRepository<PlatformStation> _stations;
    private readonly IRepository<PlatformCommand> _commands;
    private readonly IRepository<User> _users;
    private readonly IRepository<Dept> _depts;
    private readonly IIdGenerator _idGenerator;
    private readonly IConfiguration _configuration;
    private readonly AuthService _authorization;
    private readonly IDataScopeProvider _dataScope;
    private readonly IAuditLogService _audit;
    private readonly IRealtimeEventBus _realtime;

    public PlatformRecordersController(
        IRepository<PlatformRecorder> recorders,
        IRepository<PlatformFileMetadata> files,
        IRepository<PlatformStation> stations,
        IRepository<PlatformCommand> commands,
        IRepository<User> users,
        IRepository<Dept> depts,
        IIdGenerator idGenerator,
        IConfiguration configuration,
        AuthService authorization,
        IDataScopeProvider dataScope,
        IAuditLogService audit,
        IRealtimeEventBus realtime)
    {
        _recorders = recorders;
        _files = files;
        _stations = stations;
        _commands = commands;
        _users = users;
        _depts = depts;
        _idGenerator = idGenerator;
        _configuration = configuration;
        _authorization = authorization;
        _dataScope = dataScope;
        _audit = audit;
        _realtime = realtime;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? keyword,
        [FromQuery] bool? whitelisted,
        [FromQuery] bool? bound,
        [FromQuery] bool? warning,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20)
    {
        if (!await RequirePermissionAsync(PermissionCodes.RecorderView))
        {
            return StatusCode(403, new { message = "无记录仪查看权限" });
        }

        var scope = await GetScopeAsync();
        var query = _recorders.AsQueryable().Where(r =>
            string.IsNullOrWhiteSpace(keyword) ||
            r.RecorderSerial.Contains(keyword) ||
            (r.BoundUserNo != null && r.BoundUserNo.Contains(keyword)) ||
            (r.BoundUserName != null && r.BoundUserName.Contains(keyword)) ||
            (r.BoundDeptCode != null && r.BoundDeptCode.Contains(keyword)));
        if (whitelisted is { } whitelistValue)
        {
            query = query.Where(r => r.IsWhitelisted == whitelistValue);
        }

        if (bound == true)
        {
            query = query.Where(r => r.BoundUserNo != null);
        }

        var idleCutoff = DateTime.Now.AddDays(-IdleDays);
        if (warning == true)
        {
            query = query.Where(r => r.BoundUserNo == null || !r.IsWhitelisted || r.LastSeenAt < idleCutoff);
        }

        if (!scope.IsAll)
        {
            query = query.Where(r => r.DeptId != null && scope.AllowedDeptIds.Contains(r.DeptId.Value));
        }

        var total = query.Count();
        var items = query.OrderBy(r => r.LastSeenAt, SqlSugar.OrderByType.Desc)
            .ToPageList(Math.Max(1, page), Math.Max(1, size))
            .Select(r => ToView(r, idleCutoff))
            .ToList();
        return Ok(ApiResponse<PagedResult<RecorderView>>.Ok(new PagedResult<RecorderView>(page, size, total, items)));
    }

    /// <summary>使用轨迹：近 N 天按日使用量 + 按采集站聚合（数据范围过滤）。</summary>
    [HttpGet("{recorderId:long}/trail")]
    public async Task<IActionResult> Trail(long recorderId, [FromQuery] int days = 30)
    {
        if (!await RequirePermissionAsync(PermissionCodes.RecorderView))
        {
            return StatusCode(403, new { message = "无记录仪查看权限" });
        }

        var recorder = await GetInScopeAsync(recorderId);
        if (recorder is null)
        {
            return NotFound(new { message = "记录仪不存在" });
        }

        var scope = await GetScopeAsync();
        days = Math.Clamp(days, 1, 365);
        var start = DateTime.Today.AddDays(-(days - 1));
        var query = _files.AsQueryable()
            .Where(f => f.RecorderSerial == recorder.RecorderSerial && f.CollectedAt >= start);
        if (!scope.IsAll)
        {
            query = query.Where(f => f.DeptId != null && scope.AllowedDeptIds.Contains(f.DeptId.Value));
        }

        var rows = await query.Select(f => new { f.StationId, f.CollectedAt, f.Size }).ToListAsync();
        var byDay = rows.GroupBy(f => f.CollectedAt.Date)
            .ToDictionary(g => g.Key, g => (Count: g.LongCount(), Size: g.Sum(x => x.Size)));
        var dayPoints = Enumerable.Range(0, days).Select(i =>
        {
            var date = start.AddDays(i);
            byDay.TryGetValue(date, out var agg);
            return new RecorderTrailDayView(date, agg.Count, agg.Size);
        }).ToList();

        var stationIds = rows.Select(f => f.StationId).Distinct().ToList();
        var stationMap = (await _stations.GetListAsync(s => stationIds.Contains(s.Id)))
            .ToDictionary(s => s.Id, s => s.StationCode);
        var byStation = rows.GroupBy(f => f.StationId)
            .Select(g => new RecorderTrailStationView(
                g.Key,
                stationMap.TryGetValue(g.Key, out var code) ? code : string.Empty,
                g.LongCount(),
                g.Sum(x => x.Size),
                g.Min(x => x.CollectedAt),
                g.Max(x => x.CollectedAt)))
            .OrderByDescending(x => x.FileCount)
            .ToList();
        return Ok(ApiResponse<RecorderTrailView>.Ok(
            new RecorderTrailView(recorder.RecorderSerial, dayPoints, byStation)));
    }

    [HttpPut("{recorderId:long}/whitelist")]
    public async Task<IActionResult> SetWhitelist(long recorderId, SetRecorderWhitelistRequest request)
    {
        if (!await RequirePermissionAsync(PermissionCodes.RecorderManage))
        {
            return StatusCode(403, new { message = "无记录仪管理权限" });
        }

        var recorder = await GetInScopeAsync(recorderId);
        if (recorder is null)
        {
            return NotFound(new { message = "记录仪不存在" });
        }

        recorder.IsWhitelisted = request.IsWhitelisted;
        recorder.UpdatedAt = DateTime.Now;
        await _recorders.UpdateAsync(recorder);
        await WriteAuditAsync("recorder.whitelist", recorder.RecorderSerial,
            $"{(request.IsWhitelisted ? "加入" : "移出")}白名单");
        await _realtime.PublishAsync(new StationRealtimeEvent(
            RealtimeEventTypes.RecorderWhitelist, recorder.LastStationId, recorder.DeptId, DateTime.Now));
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPut("{recorderId:long}/bind")]
    public async Task<IActionResult> Bind(long recorderId, BindRecorderRequest request)
    {
        if (!await RequirePermissionAsync(PermissionCodes.RecorderManage))
        {
            return StatusCode(403, new { message = "无记录仪管理权限" });
        }

        var recorder = await GetInScopeAsync(recorderId);
        if (recorder is null)
        {
            return NotFound(new { message = "记录仪不存在" });
        }

        var user = string.IsNullOrWhiteSpace(request.UserNo)
            ? null
            : await _users.FirstAsync(u => u.UserNo == request.UserNo.Trim() && u.IsActive);
        if (user is null)
        {
            return BadRequest(new { message = "用户不存在或已停用" });
        }

        var dept = await _depts.GetByIdAsync(request.DeptId ?? user.DeptId);
        if (dept is null || !dept.IsActive)
        {
            return BadRequest(new { message = "部门不存在或已停用" });
        }

        var boundAt = DateTime.Now;
        recorder.BoundUserNo = user.UserNo;
        recorder.BoundUserName = user.Name;
        recorder.BoundDeptCode = dept.Code;
        recorder.BoundDeptName = dept.Name;
        recorder.BoundAt = boundAt;
        recorder.UpdatedAt = boundAt;
        await _recorders.UpdateAsync(recorder);

        long? commandId = null;
        if (recorder.LastStationId is { } stationId)
        {
            var payload = new WriteBindingPayload(
                recorder.RecorderSerial,
                null,
                recorder.Protocol is null ? null : (int)recorder.Protocol,
                user.UserNo,
                user.Name,
                dept.Code,
                dept.Name,
                dept.Id,
                boundAt);
            var remote = new RemoteCommand
            {
                CommandId = _idGenerator.NextId(),
                StationId = stationId,
                Type = CommandType.WriteBinding,
                PayloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }),
                IssuedAt = DateTime.Now,
                TimeoutSeconds = 300,
                Signature = string.Empty
            };
            await _commands.InsertAsync(new PlatformCommand
            {
                Id = remote.CommandId,
                StationId = stationId,
                Type = CommandType.WriteBinding,
                PayloadJson = remote.PayloadJson,
                Status = CommandStatus.Pending,
                IssuedAt = remote.IssuedAt,
                TimeoutSeconds = 300,
                Signature = PlatformCommandKeys.Sign(remote, _configuration)
            });
            commandId = remote.CommandId;
        }

        await WriteAuditAsync("recorder.bind", recorder.RecorderSerial,
            $"绑定用户 {user.Name}（{dept.Name}）");
        return Ok(ApiResponse<RecorderBindResult>.Ok(new RecorderBindResult(true, commandId)));
    }

    private async Task<PlatformRecorder?> GetInScopeAsync(long recorderId)
    {
        var recorder = await _recorders.GetByIdAsync(recorderId);
        if (recorder is null)
        {
            return null;
        }

        var scope = await GetScopeAsync();
        if (!scope.IsAll && (recorder.DeptId is null || !scope.AllowedDeptIds.Contains(recorder.DeptId.Value)))
        {
            return null;
        }

        return recorder;
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

    private Task<DataScopeResult> GetScopeAsync() =>
        DataScopeHelper.GetScopeAsync(User, _authorization, _dataScope);

    private async Task WriteAuditAsync(string operationType, string target, string? detail)
    {
        var session = User.FindFirst("accountId") is { } accountClaim &&
                      long.TryParse(accountClaim.Value, out var accountId)
            ? await _authorization.GetSessionAsync(accountId)
            : null;
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

    private static RecorderView ToView(PlatformRecorder r, DateTime idleCutoff) => new(
        r.Id, r.RecorderSerial, r.LastStationId, r.DeptId, r.Protocol,
        r.FirstSeenAt, r.LastSeenAt, r.FileCount, r.TotalSize, r.LastFileAt,
        r.BoundUserNo, r.BoundUserName, r.BoundDeptCode, r.BoundDeptName, r.BoundAt,
        r.IsWhitelisted, r.IsActive, r.UpdatedAt, Warnings(r, idleCutoff));

    private static List<string> Warnings(PlatformRecorder r, DateTime idleCutoff)
    {
        var warnings = new List<string>();
        if (string.IsNullOrEmpty(r.BoundUserNo))
        {
            warnings.Add("no_binding");
        }

        if (!r.IsWhitelisted)
        {
            warnings.Add("not_whitelisted");
        }

        if (r.LastSeenAt < idleCutoff)
        {
            warnings.Add("idle");
        }

        return warnings;
    }

    /// <summary>生命周期预警阈值：记录仪 N 天未上报视为"长期未使用"。</summary>
    private int IdleDays => _configuration.GetValue("Platform:RecorderIdleDays", 30);
}

public sealed record SetRecorderWhitelistRequest(bool IsWhitelisted);

public sealed record BindRecorderRequest(string UserNo, long? DeptId = null);

public sealed record RecorderBindResult(bool Dispatched, long? CommandId);

public sealed record RecorderView(
    long Id,
    string RecorderSerial,
    long? LastStationId,
    long? DeptId,
    ProtocolType? Protocol,
    DateTime FirstSeenAt,
    DateTime LastSeenAt,
    long FileCount,
    long TotalSize,
    DateTime? LastFileAt,
    string? BoundUserNo,
    string? BoundUserName,
    string? BoundDeptCode,
    string? BoundDeptName,
    DateTime? BoundAt,
    bool IsWhitelisted,
    bool IsActive,
    DateTime UpdatedAt,
    IReadOnlyList<string> LifecycleWarnings);

public sealed record RecorderTrailDayView(DateTime Date, long FileCount, long Size);

public sealed record RecorderTrailStationView(
    long StationId,
    string StationCode,
    long FileCount,
    long TotalSize,
    DateTime FirstSeenAt,
    DateTime LastSeenAt);

public sealed record RecorderTrailView(
    string RecorderSerial,
    List<RecorderTrailDayView> ByDay,
    List<RecorderTrailStationView> ByStation);
