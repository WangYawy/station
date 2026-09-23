namespace Station.Application.Diagnostics;

/// <summary>
/// 启动自检服务：应用启动时检查关键依赖是否就绪。
/// 失败不静默——必须显式提示用户。
/// </summary>
public interface IStartupSelfCheckService
{
    /// <summary>执行自检，返回报告。不抛异常（内部捕获）。</summary>
    Task<SelfCheckReport> RunAsync(CancellationToken ct = default);
}

/// <summary>自检报告。</summary>
public sealed record SelfCheckReport(
    bool IsHealthy,
    IReadOnlyList<SelfCheckItem> Items,
    DateTime CheckedAtUtc)
{
    /// <summary>汇总失败摘要（用于 UI 顶部展示）。</summary>
    public string FailureSummary =>
        IsHealthy
            ? "自检通过"
            : string.Join("；", Items.Where(i => !i.Ok).Select(i => i.Name));
}

/// <summary>单项检查结果。</summary>
public sealed record SelfCheckItem(
    string Name,
    bool Ok,
    string Message,
    string? Remediation = null)
{
    public string StatusText => Ok ? "✔" : "✘";
}
