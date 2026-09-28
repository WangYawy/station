using Station.Crypto;
using Station.Crypto.Engine.Formats;
using Station.Crypto.Engine.Keys;
using Station.Crypto.Formats;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Crypto;

/// <summary>STFE 文件加解密工具。</summary>
public static class StfeFileTool
{
    public static async Task<ToolResult<long>> EncryptAsync(
        string inputPath,
        string outputPath,
        MasterKeyFileDto keyFile,
        string algorithm,
        int chunkSize = StfeFormat.DefaultChunkSize,
        CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(inputPath))
                return ToolResult<long>.Fail("FILE_NOT_FOUND", $"输入文件不存在：{inputPath}");

            var version = keyFile.Current;
            var fileKey = Station.Crypto.Engine.Kdf.HkdfSm3.Derive(
                keyFile.GetKey(version), HkdfInfo.FileEncryption, 32);

            var size = await StfeFileEncryptor.EncryptAsync(
                inputPath, outputPath, fileKey, version, algorithm, chunkSize, ct);

            return ToolResult<long>.Ok(size);
        }
        catch (Exception ex)
        {
            return ToolResult<long>.Fail("ENCRYPT_FAIL", ex.Message);
        }
    }

    public static async Task<ToolResult<long>> DecryptAsync(
        string inputPath,
        string outputPath,
        MasterKeyFileDto keyFile,
        CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(inputPath))
                return ToolResult<long>.Fail("FILE_NOT_FOUND", $"输入文件不存在：{inputPath}");

            var size = await StfeFileEncryptor.DecryptAsync(
                inputPath, outputPath, keyFile.GetKey, algorithm: string.Empty, ct);

            return ToolResult<long>.Ok(size);
        }
        catch (Exception ex)
        {
            return ToolResult<long>.Fail("DECRYPT_FAIL", ex.Message);
        }
    }

    /// <summary>获取文件明文的流式解密（上传场景）。</summary>
    public static IAsyncEnumerable<ReadOnlyMemory<byte>> DecryptChunksAsync(
        string inputPath,
        MasterKeyFileDto keyFile,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keyFile);
        var provider = new DtoMasterKeyProvider(keyFile);
        return StfeAsyncChunks.DecryptChunksAsync(inputPath, provider, ct);
    }

    public static ToolResult<StfeHeader> Inspect(string filePath)
    {
        try
        {
            var header = StfeFileEncryptor.TryReadHeader(filePath);
            return header is null
                ? ToolResult<StfeHeader>.Fail("NOT_STFE", "不是 STFE 文件")
                : ToolResult<StfeHeader>.Ok(header);
        }
        catch (Exception ex)
        {
            return ToolResult<StfeHeader>.Fail("INSPECT_FAIL", ex.Message);
        }
    }
}
