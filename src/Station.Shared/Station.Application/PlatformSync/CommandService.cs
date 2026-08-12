using System.Text.Json;
using Station.Contracts.Commands;

namespace Station.Application.PlatformSync;

/// <summary>回执上报信封：包含站编号（路由需要）与回执体。</summary>
public sealed record CommandResultEnvelope(long StationId, CommandExecutionResult Result);

public sealed class CommandService : ICommandService
{
    private readonly IPlatformClient _client;
    private readonly ICommandExecutor _executor;
    private readonly ISyncOutboxService _outbox;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public CommandService(
        IPlatformClient client,
        ICommandExecutor executor,
        ISyncOutboxService outbox)
    {
        _client = client;
        _executor = executor;
        _outbox = outbox;
    }

    public async Task<int> PollAndExecuteAsync(long stationId)
    {
        var commands = await _client.PollCommandsAsync(stationId, CancellationToken.None);
        foreach (var command in commands)
        {
            var result = await _executor.ExecuteAsync(command);
            await _outbox.EnqueueAsync(
                "command-result",
                JsonSerializer.Serialize(new CommandResultEnvelope(stationId, result), _json));
        }

        return commands.Count;
    }
}
