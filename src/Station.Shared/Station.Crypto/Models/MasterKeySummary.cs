namespace Station.Crypto;

/// <summary>主密钥摘要信息（Tools 展示用）。</summary>
/// <param name="Current">当前版本号。</param>
/// <param name="Versions">所有版本号（升序）。</param>
/// <param name="CreatedAt">最早版本的创建时间。</param>
public sealed record MasterKeySummary(
    int Current,
    int[] Versions,
    DateTime CreatedAt);
