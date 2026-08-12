namespace Station.Contracts.Commands;

/// <summary>平台下发的远程指令（SM2 签名，采集站验签后执行）。</summary>
public sealed record RemoteCommand
{
    public long CommandId { get; init; }

    public long StationId { get; init; }

    public CommandType Type { get; init; }

    public string? PayloadJson { get; init; }

    public DateTime IssuedAt { get; init; }

    /// <summary>执行超时（秒），超时按失败处理、人工重发。</summary>
    public int TimeoutSeconds { get; init; } = 300;

    /// <summary>SM2 签名（Base64）。</summary>
    public required string Signature { get; init; }
}
