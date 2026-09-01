using Org.BouncyCastle.Crypto.Digests;
using Station.Domain.Security;

namespace Station.Infrastructure.Security;
public sealed class Sm3HashService : IHashService
{
    public string ComputeHash(string text)
    {
        var digest = new SM3Digest();
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        digest.BlockUpdate(bytes, 0, bytes.Length);
        var output = new byte[digest.GetDigestSize()];
        digest.DoFinal(output, 0);
        return Convert.ToHexString(output).ToLowerInvariant();
    }


    public string ComputeFileHash(string filePath) => Sm3Checksum.ComputeFile(filePath);
    public string ComputeStringHash(string text) => Sm3Checksum.ComputeString(text);
}
