using Station.Contracts.Commands;

namespace Station.Application.PlatformSync;

/// <summary>指令轮询、执行、回执。</summary>
public interface ICommandService
{
    Task<int> PollAndExecuteAsync(long stationId);
}

/// <summary>指令执行器（P0 六类指令）。</summary>
public interface ICommandExecutor
{
    Task<CommandExecutionResult> ExecuteAsync(RemoteCommand command);
}
