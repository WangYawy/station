namespace Station.Application.Storage;

///// <summary>单个存储目标的上传结果明细。</summary>
/// <summary>
/// 单个存储目标的上传结果明细
/// </summary>
/// <param name="TargetName">目标名</param>
/// <param name="Success">该 target 是否成功</param>
/// <param name="RemotePath">单 target 的远端路径</param>
/// <param name="ElapsedMs">耗时</param>
/// <param name="Attempts">尝试次数</param>
/// <param name="CircuitOpen">该 target 是否熔断</param>
/// <param name="ErrorCode">错误码</param>
/// <param name="ErrorMessage">错误信息</param>
public sealed record TargetUploadResult(
    string TargetName,
    bool Success,
    string? RemotePath,
    long ElapsedMs,
    int Attempts,
    bool CircuitOpen,
    string? ErrorCode,
    string? ErrorMessage);
