using Station.Crypto.KeyGen;
using Station.Crypto.Providers.Encryptors;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Crypto;

/// <summary>对称加解密工具（基于主密钥文件）。</summary>
public static class EncryptTool
{
    public static ToolResult<string> Encrypt(string plaintext, MasterKeyFile keyFile, string algorithm, string? aad = null)
    {
        try
        {
            var encryptor = ResolveEncryptor(keyFile, algorithm);
            return ToolResult<string>.Ok(encryptor.Encrypt(plaintext, aad));
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("ENCRYPT_FAIL", ex.Message);
        }
    }

    public static ToolResult<string> Decrypt(string ciphertext, MasterKeyFile keyFile, string algorithm, string? aad = null)
    {
        try
        {
            var encryptor = ResolveEncryptor(keyFile, algorithm);
            return ToolResult<string>.Ok(encryptor.Decrypt(ciphertext, aad));
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("DECRYPT_FAIL", ex.Message);
        }
    }

    internal static Station.Crypto.Abstractions.IEncryptor ResolveEncryptor(MasterKeyFile keyFile, string algorithm) =>
        algorithm.ToUpperInvariant() switch
        {
            "SM4-GCM" or "SM4" => new Sm4GcmEncryptor(
                () => keyFile.Current,
                v => keyFile.GetKey(v)),
            "AES-256-GCM" or "AES" => new AesGcmEncryptor(
                () => keyFile.Current,
                v => keyFile.GetKey(v)),
            _ => throw new NotSupportedException($"不支持的加密算法：{algorithm}")
        };
}
