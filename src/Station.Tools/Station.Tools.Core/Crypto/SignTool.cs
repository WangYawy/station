using System.Text;
using Station.Crypto.Engine.Internal;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Crypto;

/// <summary>签名/验签工具。</summary>
public static class SignTool
{
    public static ToolResult<string> Sign(string text, string privateKeyPem, string algorithm)
    {
        try
        {
            var signer = AlgorithmRegistry.Default.GetSigner(algorithm);
            return ToolResult<string>.Ok(
                signer.Sign(Encoding.UTF8.GetBytes(text), privateKeyPem));
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("SIGN_FAIL", ex.Message);
        }
    }

    public static ToolResult<bool> Verify(
        string text, string signature, string publicKeyPem, string algorithm)
    {
        try
        {
            var signer = AlgorithmRegistry.Default.GetSigner(algorithm);
            var ok = signer.Verify(Encoding.UTF8.GetBytes(text), signature, publicKeyPem);
            return ToolResult<bool>.Ok(ok);
        }
        catch (Exception ex)
        {
            return ToolResult<bool>.Fail("VERIFY_FAIL", ex.Message);
        }
    }

    public static async Task<ToolResult<string>> SignFileAsync(
        string filePath, string privateKeyPem, string algorithm, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(filePath))
                return ToolResult<string>.Fail("FILE_NOT_FOUND", $"文件不存在：{filePath}");

            var data = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
            var signer = AlgorithmRegistry.Default.GetSigner(algorithm);
            return ToolResult<string>.Ok(signer.Sign(data, privateKeyPem));
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("SIGN_FAIL", ex.Message);
        }
    }
}
