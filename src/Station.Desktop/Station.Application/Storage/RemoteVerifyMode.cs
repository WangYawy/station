namespace Station.Application.Storage;

/// <summary>
/// 远端校验模式。用于平衡"正确性"与"网络开销"。
/// </summary>
public enum RemoteVerifyMode
{
    /// <summary>只校验大小（默认，最快）。</summary>
    None = 0,

    /// <summary>大小 + 抽样校验（首/中/尾 3 个 4KB 块）。</summary>
    SizeAndSample = 1,

    /// <summary>完整 SM3（下载重算，最慢但最强）。</summary>
    Full = 2
}
