using System.Text.Json;
using Station.Contracts;
using Station.Contracts.Commands;
using Microsoft.Extensions.Logging;

namespace Station.Application.PlatformSync;

/// <summary>回执上报信封：包含站编号（路由需要）与回执体。</summary>
public sealed record CommandResultEnvelope(long StationId, CommandExecutionResult Result);

public sealed class CommandService : ICommandService
{
    private readonly IPlatformClient _client;
    private readonly ICommandExecutor _executor;
    private readonly ISyncOutboxService _outbox;
    private readonly ICommandSignatureVerifier _verifier;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly ILogger<CommandService> _logger;

    public CommandService(
        IPlatformClient client,
        ICommandExecutor executor,
        ISyncOutboxService outbox,
        ICommandSignatureVerifier verifier,
        ILogger<CommandService> logger)
    {
        _client = client;
        _executor = executor;
        _outbox = outbox;
        _verifier = verifier;
        _logger = logger;
    }

    public async Task<int> PollAndExecuteAsync(long stationId)
    {
        var commands = await _client.PollCommandsAsync(stationId, CancellationToken.None);
        foreach (var command in commands)
        {
            if (!_verifier.Verify(command))
            {
                _logger.LogWarning("指令 {CommandId} 签名校验失败，拒绝执行（类型 {Type}）", command.CommandId, command.Type);
                await _outbox.EnqueueAsync(
                    "command-result",
                    System.Text.Json.JsonSerializer.Serialize(new CommandResultEnvelope(stationId,
                        new CommandExecutionResult
                        {
                            CommandId = command.CommandId,
                            Status = CommandStatus.Failed,
                            StartedAt = DateTime.Now,
                            FinishedAt = DateTime.Now,
                            Message = "指令签名无效，拒绝执行"
                        })));
                continue;
            }

            var result = await _executor.ExecuteAsync(command);
            await _outbox.EnqueueAsync(
                "command-result",
                JsonSerializer.Serialize(new CommandResultEnvelope(stationId, result), _json));
        }

        return commands.Count;
    }
}
