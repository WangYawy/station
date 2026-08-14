using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Station.Contracts;
using Station.Contracts.Api;
using Station.Contracts.Commands;
using Station.Application.Authorization;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;
using AuthService = Station.Application.Authorization.IAuthorizationService;

namespace Station.Platform.Api.Controllers;

/// <summary>平台指令下发与执行状态查询。</summary>
[ApiController]
[Authorize]
[Route("api/v1/stations/{stationId:long}/commands")]
public class PlatformCommandsController : ControllerBase
{
    private readonly IRepository<PlatformCommand> _commands;
    private readonly IRepository<PlatformStation> _stations;
    private readonly IIdGenerator _idGenerator;
    private readonly IConfiguration _configuration;
    private readonly AuthService _authorization;
    private readonly IDataScopeProvider _dataScope;

    public PlatformCommandsController(
        IRepository<PlatformCommand> commands,
        IRepository<PlatformStation> stations,
        IIdGenerator idGenerator,
        IConfiguration configuration,
        AuthService authorization,
        IDataScopeProvider dataScope)
    {
        _commands = commands;
        _stations = stations;
        _idGenerator = idGenerator;
        _configuration = configuration;
        _authorization = authorization;
        _dataScope = dataScope;
    }

    /// <summary>下发指令（P0 六类）。</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<CommandExecutionResult>>> Dispatch(
        long stationId,
        DispatchCommandRequest request)
    {
        var station = await _stations.GetByIdAsync(stationId);
        if (station is null)
        {
            return NotFound(new { message = "采集站不存在" });
        }

        var scope = await DataScopeHelper.GetScopeAsync(User, _authorization, _dataScope);
        if (!scope.IsAll && (station.DeptId is null || !scope.AllowedDeptIds.Contains(station.DeptId.Value)))
        {
            return StatusCode(403, new { message = "无权向该采集站下发指令" });
        }

        var remote = new RemoteCommand
        {
            CommandId = _idGenerator.NextId(),
            StationId = stationId,
            Type = request.Type,
            PayloadJson = request.PayloadJson,
            IssuedAt = DateTime.Now,
            TimeoutSeconds = request.TimeoutSeconds,
            Signature = string.Empty
        };
        var command = new PlatformCommand
        {
            Id = remote.CommandId,
            StationId = stationId,
            Type = request.Type,
            PayloadJson = request.PayloadJson,
            Status = CommandStatus.Pending,
            IssuedAt = remote.IssuedAt,
            TimeoutSeconds = request.TimeoutSeconds,
            Signature = PlatformCommandKeys.Sign(remote, _configuration)
        };
        await _commands.InsertAsync(command);
        return Ok(ApiResponse<CommandExecutionResult>.Ok(new CommandExecutionResult
        {
            CommandId = command.Id,
            Status = CommandStatus.Pending,
            Message = "指令已下发"
        }));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<CommandView>>>> List(long stationId)
    {
        var list = (await _commands.GetListAsync(c => c.StationId == stationId))
            .OrderByDescending(c => c.IssuedAt)
            .Take(50)
            .Select(c => new CommandView
            {
                CommandId = c.Id,
                Type = c.Type,
                Status = c.Status,
                IssuedAt = c.IssuedAt,
                FinishedAt = c.ExecutedAt,
                Message = c.ResultMessage
            })
            .ToList();
        return Ok(ApiResponse<List<CommandView>>.Ok(list));
    }
}

/// <summary>指令下发请求。</summary>
public sealed record DispatchCommandRequest(
    CommandType Type,
    string? PayloadJson = null,
    int TimeoutSeconds = 300);

/// <summary>指令视图（含类型/状态/回执）。</summary>
public sealed class CommandView
{
    public long CommandId { get; set; }

    public CommandType Type { get; set; }

    public CommandStatus Status { get; set; }

    public DateTime IssuedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public string? Message { get; set; }
}
