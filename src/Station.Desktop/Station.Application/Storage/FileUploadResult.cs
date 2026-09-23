namespace Station.Application.Storage;

/// <summary>
/// 文件上传总体结果。
/// 
/// 【字段顺序固定】
///   1. Success              是否整体成功（按 MultiTargetMode 判定）
///   2. PrimaryRemotePath    主远端路径（仅成功时非空）
///   3. ErrorMessage         失败时的汇总错误信息
///   4. RetryCount           发生的最大重试次数
///   5. CircuitOpen          是否有 target 触发熔断
///   6. TargetResults        每个 target 的明细结果
/// </summary>
public sealed record FileUploadResult(
    bool Success,
    string? PrimaryRemotePath,
    string? ErrorMessage,
    int RetryCount,
    bool CircuitOpen,
    IReadOnlyList<TargetUploadResult> TargetResults);
