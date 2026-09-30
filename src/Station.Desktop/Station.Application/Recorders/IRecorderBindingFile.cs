using Station.Crypto;

namespace Station.Application.Recorders;

/// <summary>
/// 记录仪绑定文件（station_bind.ini）门面。
///
/// 【职责分离】
///   - IO：读写记录仪根目录
///   - 策略：从 ICryptoPolicyService 获取 MAC 算法 + Binding 密钥
///   - 协议：交由 Station.Crypto.BindingFileCodec 处理（唯一实现）
/// </summary>
public interface IRecorderBindingFile
{
    /// <summary>写入绑定文件（自动计算 MAC）。</summary>
    Task WriteAsync(
        string recorderRootPath,
        BindingInfo binding,
        CancellationToken ct = default);

    /// <summary>读取绑定文件（不验证 MAC）。</summary>
    BindingInfo? Read(string recorderRootPath);

    /// <summary>验证绑定文件 MAC。</summary>
    Task<bool> VerifySignatureAsync(
        string recorderRootPath,
        CancellationToken ct = default);

    /// <summary>删除绑定文件。</summary>
    void Delete(string recorderRootPath);
}
