using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace Station.Crypto.Engine.Kdf;

/// <summary>
/// HKDF-SM3 密钥派生。
///
/// ⚠️ info 参数必须使用 <see cref="HkdfInfo"/> 中的常量，
///    禁止在调用处硬编码字符串。
/// </summary>
public static class HkdfSm3
{
    /// <summary>
    /// 从主密钥派生指定长度的子密钥。
    /// </summary>
    /// <param name="ikm">输入密钥材料（主密钥，32 字节）。</param>
    /// <param name="info">用途标识（见 <see cref="HkdfInfo"/>）。</param>
    /// <param name="length">输出长度（默认 32 字节）。</param>
    /// <exception cref="ArgumentException">ikm 为空，或 info 为空白。</exception>
    public static byte[] Derive(byte[] ikm, string info, int length = 32)
    {
        if (ikm is null || ikm.Length == 0)
            throw new ArgumentException("IKM 不能为空", nameof(ikm));
        if (string.IsNullOrWhiteSpace(info))
            throw new ArgumentException("info 不能为空", nameof(info));
        if (length <= 0 || length > 255 * 32)
            throw new ArgumentOutOfRangeException(nameof(length), "长度必须在 (0, 8160] 之间");

        var hkdf = new HkdfBytesGenerator(new SM3Digest());
        hkdf.Init(new HkdfParameters(ikm, (byte[]?)null, Encoding.UTF8.GetBytes(info)));

        var output = new byte[length];
        hkdf.GenerateBytes(output, 0, length);
        return output;
    }
}
