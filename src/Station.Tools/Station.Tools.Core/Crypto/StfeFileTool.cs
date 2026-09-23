using Station.Crypto.Formats;
using Station.Crypto.KeyGen;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Crypto;

/// <summary>STFE 文件加解密工具。</summary>
public static class StfeFileTool
{
    public static async Task<ToolResult<long>> EncryptAsync(
        string inputPath,
        string outputPath,
        MasterKeyFile keyFile,
        string algorithm,
        int chunkSize = StfeFileEncryptor.DefaultChunkSize,
        CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(inputPath))
                return ToolResult<long>.Fail("FILE_NOT_FOUND", $"输入文件不存在：{inputPath}");

            var key = keyFile.GetKey(keyFile.Current);
            var size = await StfeFileEncryptor.EncryptAsync(
                inputPath, outputPath, key, keyFile.Current, algorithm, chunkSize, ct);

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
        MasterKeyFile keyFile,
        string algorithm,
        CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(inputPath))
                return ToolResult<long>.Fail("FILE_NOT_FOUND", $"输入文件不存在：{inputPath}");

            var size = await StfeFileEncryptor.DecryptAsync(
                inputPath, outputPath, keyFile.GetKey, algorithm, ct);

            return ToolResult<long>.Ok(size);
        }
        catch (Exception ex)
        {
            return ToolResult<long>.Fail("DECRYPT_FAIL", ex.Message);
        }
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
