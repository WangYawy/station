using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Station.Contracts;
using Station.Contracts.Api;
using Station.Contracts.Commands;
using Station.Infrastructure.IdGenerators;
using Station.Infrastructure.Repositories;
using Station.Infrastructure.Security;
using Station.Platform.Domain.Entities;

namespace Station.Platform.Api.Controllers;

/// <summary>平台指令下发与执行状态查询。</summary>
[ApiController]
[Route("api/v1/stations/{stationId:long}/commands")]
public class PlatformCommandsController : ControllerBase
{
    private readonly IRepository<PlatformCommand> _commands;
    private readonly IIdGenerator _idGenerator;
    private readonly IConfiguration _configuration;

    public PlatformCommandsController(
        IRepository<PlatformCommand> commands,
        IIdGenerator idGenerator,
        IConfiguration configuration)
    {
        _commands = commands;
        _idGenerator = idGenerator;
        _configuration = configuration;
    }

    /// <summary>下发指令（P0 六类）。</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<CommandExecutionResult>>> Dispatch(
        long stationId,
        DispatchCommandRequest request)
    {
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
        var privateKey = _configuration["Platform:Command:PrivateKeyPem"];
        if (string.IsNullOrWhiteSpace(privateKey) &&
            _configuration["Platform:Command:PrivateKeyPemFile"] is { } pemFile &&
            System.IO.File.Exists(pemFile))
        {
            privateKey = System.IO.File.ReadAllText(pemFile).Trim();
        }
        var command = new PlatformCommand
        {
            Id = remote.CommandId,
            StationId = stationId,
            Type = request.Type,
            PayloadJson = request.PayloadJson,
            Status = CommandStatus.Pending,
            IssuedAt = remote.IssuedAt,
            TimeoutSeconds = request.TimeoutSeconds,
            Signature = string.IsNullOrWhiteSpace(privateKey)
                ? "unsigned"
                : Sm2LicenseSigner.Sign(privateKey, RemoteCommandSignature.Canonical(remote))
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
