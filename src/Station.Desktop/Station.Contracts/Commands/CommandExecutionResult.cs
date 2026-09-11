namespace Station.Contracts.Commands;

/// <summary>远程指令执行结果回执。</summary>
public sealed record CommandExecutionResult
{
    public long CommandId { get; init; }

    public CommandStatus Status { get; init; }

    public DateTime? StartedAt { get; init; }

    public DateTime? FinishedAt { get; init; }

    public string? Message { get; init; }
}
