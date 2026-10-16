using Station.Device.Detection;

namespace Station.Application.Recorders;

/// <summary>记录仪识别服务：四层校验，产出 BoundDevice。</summary>
public interface IRecorderIdentificationService
{
    /// <summary>
    /// 识别接入设备：读绑定文件 → 验签 → 台账校验 → 用户/部门校验。
    /// 设备根路径从 <see cref="DetectedDevice.Root"/> 获取。
    /// </summary>
    Task<RecorderIdentifyResult> IdentifyAsync(
        DetectedDevice device, CancellationToken ct = default);
}
