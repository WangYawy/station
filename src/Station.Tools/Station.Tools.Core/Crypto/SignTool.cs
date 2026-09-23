using System.Text;
using Station.Crypto.Providers.Signers;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Crypto;

/// <summary>签名/验签工具。</summary>
public static class SignTool
{
    public static ToolResult<string> Sign(string text, string privateKeyPem, string algorithm)
    {
        try
        {
            var signer = ResolveSigner(algorithm);
            var sig = signer.Sign(Encoding.UTF8.GetBytes(text), privateKeyPem);
            return ToolResult<string>.Ok(sig);
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("SIGN_FAIL", ex.Message);
        }
    }

    public static ToolResult<bool> Verify(string text, string signature, string publicKeyPem, string algorithm)
    {
        try
        {
            var signer = ResolveSigner(algorithm);
            var ok = signer.Verify(Encoding.UTF8.GetBytes(text), signature, publicKeyPem);
            return ToolResult<bool>.Ok(ok);
        }
        catch (Exception ex)
        {
            return ToolResult<bool>.Fail("VERIFY_FAIL", ex.Message);
        }
    }

    /// <summary>对文件签名。</summary>
    public static async Task<ToolResult<string>> SignFileAsync(
        string filePath, string privateKeyPem, string algorithm, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(filePath))
                return ToolResult<string>.Fail("FILE_NOT_FOUND", $"文件不存在：{filePath}");

            var data = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
            var signer = ResolveSigner(algorithm);
            var sig = signer.Sign(data, privateKeyPem);
            return ToolResult<string>.Ok(sig);
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("SIGN_FAIL", ex.Message);
        }
    }

    internal static Station.Crypto.Abstractions.ISigner ResolveSigner(string algorithm) =>
        algorithm.ToUpperInvariant() switch
        {
            "SM2-SM3" or "SM2" => new Sm2Sm3Signer(),
            "RSA-SHA256" or "RSA" => new RsaSha256Signer(),
            _ => throw new NotSupportedException($"不支持的签名算法：{algorithm}")
        };
}
