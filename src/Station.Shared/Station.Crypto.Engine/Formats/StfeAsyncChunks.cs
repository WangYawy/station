using System.Buffers;
using System.Runtime.CompilerServices;
using Station.Crypto;

namespace Station.Crypto.Engine.Formats;

/// <summary>
/// STFE 异步分块解密（上传场景友好）。
///
/// 每次 yield 一个 80KB 明文块，适合直接推送到 HTTP 流 / 上传管道。
/// 内存 O(80KB + chunkSize)。
/// </summary>
public static class StfeAsyncChunks
{
    private const int BufferSize = 81920;

    /// <summary>
    /// 异步解密 STFE 文件，逐块产出明文。
    /// </summary>
    /// <param name="sourcePath">STFE 文件路径。</param>
    /// <param name="keys">主密钥提供者。</param>
    /// <param name="ct">取消令牌。</param>
    public static async IAsyncEnumerable<ReadOnlyMemory<byte>> DecryptChunksAsync(
        string sourcePath,
        IMasterKeyProvider keys,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(keys);

        await using var stream = StfeDecryptStream.Open(sourcePath, keys);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                var n = await stream
                    .ReadAsync(buffer.AsMemory(0, BufferSize), ct)
                    .ConfigureAwait(false);

                if (n == 0) yield break;

                var copy = new byte[n];
                Buffer.BlockCopy(buffer, 0, copy, 0, n);
                yield return copy;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
