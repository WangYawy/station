namespace Station.Application.Security.Abstractions;

/// <summary>
/// 记录仪绑定密钥提供者。
/// 
/// 用途：为 Binding 文件的 MAC 提供对称密钥。
/// 实现从主密钥 HKDF 派生，或从配置读兼容值。
/// 
/// 【版本化】返回的密钥带主密钥版本号，支持轮换后读旧文件。
/// </summary>
public interface IBindingSecretProvider
{
    /// <summary>获取当前 Binding 密钥（32 字节）。</summary>
    byte[] GetSecret();

    /// <summary>当前密钥对应的主密钥版本号（用于审计/日志）。</summary>
    int GetKeyVersion();
}
