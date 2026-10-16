using Station.Crypto;
using Station.Device.Detection;

namespace Station.Application.Recorders;

/// <summary>
/// 记录仪绑定文件（station_bind.ini）读写与验签契约。
/// IO 由 <c>Station.Device.IDeviceFileSystem</c> 完成，本接口只承载业务。
/// </summary>
public interface IRecorderBindingFile
{
    /// <summary>写入绑定文件（含 MAC 计算）。</summary>
    Task WriteAsync(DetectedDevice device, BindingInfo binding, CancellationToken ct = default);

    /// <summary>读取绑定文件；不存在或格式错误返回 null。</summary>
    BindingInfo? Read(DetectedDevice device);

    /// <summary>验签绑定文件；文件不存在 / 无 Signature / 密钥不匹配返回 false。</summary>
    Task<bool> VerifySignatureAsync(DetectedDevice device, CancellationToken ct = default);

    /// <summary>删除绑定文件；不存在时静默忽略。</summary>
    void Delete(DetectedDevice device);
}
