using Station.Crypto;
using Station.Crypto.Engine.Internal;
using Station.Crypto.Engine.Keys;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Crypto;

/// <summary>对称加解密工具（基于主密钥文件）。</summary>
public static class EncryptTool
{
    public static ToolResult<string> Encrypt(
        string plaintext, MasterKeyFileDto keyFile, string algorithm, string? aad = null)
    {
        try
        {
            var encryptor = Resolve(keyFile, algorithm);
            return ToolResult<string>.Ok(encryptor.Encrypt(plaintext, aad));
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("ENCRYPT_FAIL", ex.Message);
        }
    }

    public static ToolResult<string> Decrypt(
        string ciphertext, MasterKeyFileDto keyFile, string algorithm, string? aad = null)
    {
        try
        {
            var encryptor = Resolve(keyFile, algorithm);
            return ToolResult<string>.Ok(encryptor.Decrypt(ciphertext, aad));
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("DECRYPT_FAIL", ex.Message);
        }
    }

    /// <summary>解析算法并构造 Encryptor（唯一 switch 已收敛到 AlgorithmRegistry）。</summary>
    private static IEncryptor Resolve(MasterKeyFileDto keyFile, string algorithm)
    {
        ArgumentNullException.ThrowIfNull(keyFile);
        var keys = new DtoMasterKeyProvider(keyFile);
        return AlgorithmRegistry.Default.GetEncryptor(algorithm, keys);
    }
}

/// <summary>
/// 把 MasterKeyFileDto 适配为 IMasterKeyProvider（Tools 场景，无持久化轮换需求）。
/// </summary>
internal sealed class DtoMasterKeyProvider : IMasterKeyProvider
{
    private readonly MasterKeyFileDto _dto;

    public DtoMasterKeyProvider(MasterKeyFileDto dto)
        => _dto = dto ?? throw new ArgumentNullException(nameof(dto));

    public int CurrentVersion => _dto.Current;

    public IReadOnlyList<int> GetVersions()
    {
        var v = _dto.Keys.Select(k => k.Version).OrderBy(x => x).ToArray();
        return v;
    }

    public byte[] GetKey(int version) => _dto.GetKey(version);

    public Task<int> RotateAsync(CancellationToken ct = default)
        => throw new NotSupportedException(
            "离线工具不支持轮换，请使用 key gen-master 重新生成密钥文件");

    public byte[] DeriveBindingKey()
        => Station.Crypto.Engine.Kdf.HkdfSm3.Derive(
            GetKey(CurrentVersion), HkdfInfo.RecorderBinding, 32);

    public byte[] DeriveFileEncryptionKey(int keyVersion)
        => Station.Crypto.Engine.Kdf.HkdfSm3.Derive(
            GetKey(keyVersion), HkdfInfo.FileEncryption, 32);
}
