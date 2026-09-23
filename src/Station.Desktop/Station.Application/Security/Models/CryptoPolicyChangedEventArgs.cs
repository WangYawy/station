namespace Station.Application.Security.Models;

/// <summary>策略变更事件参数。UI 监听后可刷新审计列表等。</summary>
public sealed class CryptoPolicyChangedEventArgs : EventArgs
{
    public string UsageCode { get; init; } = string.Empty;
    public string OldAlgorithm { get; init; } = string.Empty;
    public string NewAlgorithm { get; init; } = string.Empty;
    public string OperatorAccount { get; init; } = string.Empty;
}
