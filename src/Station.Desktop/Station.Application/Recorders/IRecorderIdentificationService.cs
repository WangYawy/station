using Station.Application.Collecting;
using Station.Domain.Collecting;

namespace Station.Application.Recorders;

/// <summary>
/// 记录仪接入识别与归属：ini 识别 + 台账一致性校验。
/// 
/// 【识别层级】
///   第 1 层：无 station_bind.ini → 未绑定设备，需绑定后才可采集
///   第 2 层：有文件但 MAC 验证失败 → 被篡改或旧格式，需重新绑定
///   第 3 层：MAC 有效 → 校验台账一致性（记录仪/用户/部门是否在册且启用）
/// </summary>
public interface IRecorderIdentificationService
{
    /// <summary>
    /// 识别设备接入时的绑定状态。
    /// </summary>
    /// <param name="device">设备信息（序列号、协议等）</param>
    /// <param name="recorderRootPath">记录仪根目录路径</param>
    /// <param name="ct">取消令牌</param>
    Task<RecorderIdentifyResult> IdentifyAsync(
        CollectDeviceInfo device,
        string recorderRootPath,
        CancellationToken ct = default);
}
