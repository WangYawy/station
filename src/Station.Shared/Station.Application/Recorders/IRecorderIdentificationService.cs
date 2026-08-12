using Station.Application.Collecting;

namespace Station.Application.Recorders;

/// <summary>记录仪接入识别与归属：ini 三层识别 + 台账一致性。</summary>
public interface IRecorderIdentificationService
{
    Task<RecorderIdentifyResult> IdentifyAsync(CollectDeviceInfo device, string recorderRootPath);
}
