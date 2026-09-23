namespace Station.Application.Security.Abstractions;

/// <summary>密钥轮换服务。</summary>
public interface IKeyRotationService
{
    /// <summary>
    /// 执行完整轮换流程：
    /// 1) 生成新版本主密钥并设为 current；
    /// 2) 异步重加密所有 IsEncrypted=1 的配置字段；
    /// 3) 写审计。返回结果供 UI 展示。
    /// </summary>
    Task<KeyRotationResult> RotateAsync(string operatorAccount, CancellationToken ct = default);
}

/// <summary>轮换结果。</summary>
public sealed record KeyRotationResult(
    int OldVersion,
    int NewVersion,
    int ReEncryptedCount,
    int FailedCount,
    TimeSpan Elapsed);
