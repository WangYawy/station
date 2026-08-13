using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

namespace Station.Infrastructure.Security;

/// <summary>SM3 文件/字符串校验工具（国密，采集完整性校验）。</summary>
public static class Sm3Checksum
{
    public static string ComputeFile(string path)
    {
        var digest = new SM3Digest();
        using var stream = File.OpenRead(path);
        var buffer = new byte[1024 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            digest.BlockUpdate(buffer, 0, read);
        }

        return ToHex(digest);
    }

    public static string ComputeString(string text)
    {
        var digest = new SM3Digest();
        var bytes = Encoding.UTF8.GetBytes(text);
        digest.BlockUpdate(bytes, 0, bytes.Length);
        return ToHex(digest);
    }

    /// <summary>HMAC-SM3（授权文件/敏感载荷签名）。</summary>
    public static string ComputeHmac(string key, string text)
    {
        var hmac = new HMac(new SM3Digest());
        hmac.Init(new KeyParameter(Encoding.UTF8.GetBytes(key)));
        var data = Encoding.UTF8.GetBytes(text);
        hmac.BlockUpdate(data, 0, data.Length);
        var output = new byte[hmac.GetMacSize()];
        hmac.DoFinal(output, 0);
        return Convert.ToHexString(output).ToLowerInvariant();
    }

    private static string ToHex(SM3Digest digest)
    {
        var output = new byte[digest.GetDigestSize()];
        digest.DoFinal(output, 0);
        return Convert.ToHexString(output).ToLowerInvariant();
    }
}
