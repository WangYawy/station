using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;

namespace Station.Crypto.Kdf;

/// <summary>HKDF-SM3 密钥派生。</summary>
public static class HkdfSm3
{
    /// <summary>从主密钥派生指定长度的子密钥。</summary>
    public static byte[] Derive(byte[] ikm, string info, int length = 32)
    {
        if (ikm is null || ikm.Length == 0) throw new ArgumentException("IKM 不能为空");
        if (string.IsNullOrEmpty(info)) throw new ArgumentException("info 不能为空");

        var hkdf = new HkdfBytesGenerator(new SM3Digest());
        hkdf.Init(new HkdfParameters(ikm, (byte[]?)null, Encoding.UTF8.GetBytes(info)));
        var output = new byte[length];
        hkdf.GenerateBytes(output, 0, length);
        return output;
    }
}
