using Station.Application.Collecting;

namespace Station.Application.Recorders;

/// <summary>记录仪接入识别与归属：ini 三层识别 + 台账一致性。</summary>
public interface IRecorderIdentificationService
{
    /// <summary>
    /// 设备识别
    /// </summary>
    /// <param name="device">设备</param>
    /// <param name="recorderRootPath">根目录</param>
    /// <returns></returns>
    Task<RecorderIdentifyResult> IdentifyAsync(CollectDeviceInfo device, string recorderRootPath);
}
