using System.Buffers;
using Org.BouncyCastle.Crypto.Digests;

namespace Station.Crypto.Engine.Hashers;

/// <summary>SM3 摘要。</summary>
public sealed class Sm3Hasher : IHasher
{
    public string Algorithm => CryptoAlgorithm.Sm3;

    public string ComputeHash(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var digest = new SM3Digest();
        digest.BlockUpdate(data, 0, data.Length);
        var output = new byte[digest.GetDigestSize()];
        digest.DoFinal(output, 0);
        return Convert.ToBase64String(output);
    }

    public Task<string> ComputeHashAsync(Stream stream, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Task.Run(() => ComputeHashCore(stream, ct), ct);
    }

    private static string ComputeHashCore(Stream stream, CancellationToken ct)
    {
        var digest = new SM3Digest();
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                digest.BlockUpdate(buffer, 0, read);
            }
            var output = new byte[digest.GetDigestSize()];
            digest.DoFinal(output, 0);
            return Convert.ToBase64String(output);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
