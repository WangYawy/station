namespace Station.Application.Security.Abstractions;

/// <summary>
/// 对称消息认证码（MAC）。
/// 
/// 【与 IHasher 的区别】
///   - IHasher：无密钥，任何人可算（用于完整性校验）
///   - IMacProvider：有密钥，只有持密钥者能算（用于防伪 + 完整性）
/// 
/// 【与 ISigner 的区别】
///   - ISigner：非对称，私钥签 / 公钥验
///   - IMacProvider：对称，签名与验签用同一密钥
/// 
/// 【输出】Base64 字符串。
/// </summary>
public interface IMacProvider
{
    /// <summary>算法标识（"HMAC-SM3" / "HMAC-SHA256"）。</summary>
    string Algorithm { get; }

    /// <summary>计算 MAC。</summary>
    /// <param name="key">共享密钥（32 字节以上）。</param>
    /// <param name="data">待认证数据。</param>
    string Compute(byte[] key, byte[] data);

    /// <summary>验证 MAC。使用固定时间比较防时序攻击。</summary>
    bool Verify(byte[] key, byte[] data, string macBase64);
}
