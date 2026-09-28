using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.PlatformSync;
using Station.Application.Security;
using Station.Crypto;
using Station.Crypto.Engine.Formats;
using Station.Crypto.Formats;
using Station.Infrastructure.Security.Keys;

namespace Station.Infrastructure.Security;

/// <summary>文件加密服务实现（摘要 + 签名 + STFE 加解密）。</summary>
public sealed class FileCryptoService : IFileCryptoService
{
    private readonly ICryptoPolicyService _policy;
    private readonly IMasterKeyProvider _keys;
    private readonly ICryptoAlgorithmRegistry _registry;
    private readonly PemKeyCache _keyCache;
    private readonly ReportingOptions _reportingOptions;
    private readonly ILogger<FileCryptoService> _logger;

    public FileCryptoService(
        ICryptoPolicyService policy,
        IMasterKeyProvider keys,
        ICryptoAlgorithmRegistry registry,
        PemKeyCache keyCache,
        IOptions<ReportingOptions> reportingOptions,
        ILogger<FileCryptoService> logger)
    {
        _policy = policy;
        _keys = keys;
        _registry = registry;
        _keyCache = keyCache;
        _reportingOptions = reportingOptions.Value;
        _logger = logger;
    }

    public async Task<string> GetDigestAlgorithmAsync(CancellationToken ct = default)
    {
        var policy = await _policy.GetAsync(CryptoUsage.FileSig, ct).ConfigureAwait(false);
        return policy.Algorithm;
    }

    public async Task<(string Digest, string Algorithm)> ComputeDigestAsync(
        string filePath, CancellationToken ct = default)
    {
        var policy = await _policy.GetAsync(CryptoUsage.FileSig, ct).ConfigureAwait(false);
        var hasher = await _policy.GetHasherAsync(CryptoUsage.FileSig, ct).ConfigureAwait(false);

        await using var fs = File.OpenRead(filePath);
        var digest = await hasher.ComputeHashAsync(fs, ct).ConfigureAwait(false);
        return (digest, policy.Algorithm);
    }

    public async Task<(string Signature, string Algorithm)> SignMetadataAsync(
        FileMetadata metadata, CancellationToken ct = default)
    {
        var policy = await _policy.GetAsync(CryptoUsage.FileSig, ct).ConfigureAwait(false);
        var signer = await _policy.GetSignerAsync(CryptoUsage.FileSig, ct).ConfigureAwait(false);

        var privateKeyPem = await _keyCache.GetAsync(_reportingOptions.PrivateKeyFile, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(privateKeyPem))
            throw new InvalidOperationException(
                $"未配置元数据签名私钥：{_reportingOptions.PrivateKeyFile}");

        var canonical = FileMetadataCodec.Canonical(metadata);
        var signature = signer.Sign(System.Text.Encoding.UTF8.GetBytes(canonical), privateKeyPem);
        return (signature, policy.SecondaryAlgorithm ?? policy.Algorithm);
    }

    public async Task<bool> VerifyMetadataAsync(
        FileMetadata metadata, string signature, string algorithm,
        CancellationToken ct = default)
    {
        var publicKeyPem = await _keyCache.GetAsync(_reportingOptions.PublicKeyFile, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(publicKeyPem)) return false;

        var signer = _registry.GetSigner(algorithm);
        var canonical = FileMetadataCodec.Canonical(metadata);
        return signer.Verify(
            System.Text.Encoding.UTF8.GetBytes(canonical), signature, publicKeyPem);
    }

    public async Task<long> EncryptAsync(
        string sourcePath, string targetPath, CancellationToken ct = default)
    {
        var policy = await _policy.GetAsync(CryptoUsage.FileEncryption, ct).ConfigureAwait(false);
        var keyVersion = _keys.CurrentVersion;

        return await StfeFileEncryptor.EncryptAsync(
            sourcePath, targetPath, _keys, policy.Algorithm,
            StfeFormat.DefaultChunkSize, ct).ConfigureAwait(false);
    }

    public async Task<long> EncryptInPlaceAsync(string filePath, CancellationToken ct = default)
    {
        var tempPath = filePath + ".stfe.tmp";
        try
        {
            var size = await EncryptAsync(filePath, tempPath, ct).ConfigureAwait(false);

            if (OperatingSystem.IsWindows() && File.Exists(filePath))
                File.Replace(tempPath, filePath, null);
            else
                File.Move(tempPath, filePath, overwrite: true);

            return size;
        }
        catch
        {
            if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
            throw;
        }
    }

    public Stream OpenDecryptStream(string encryptedFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedFilePath);
        return StfeDecryptStream.Open(encryptedFilePath, _keys);
    }

    public IAsyncEnumerable<ReadOnlyMemory<byte>> DecryptChunksAsync(
        string encryptedFilePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedFilePath);
        return StfeAsyncChunks.DecryptChunksAsync(encryptedFilePath, _keys, ct);
    }

    public bool IsEncrypted(string filePath) => StfeFileEncryptor.IsStfe(filePath);

    public StfeHeader? TryReadHeader(string filePath) => StfeFileEncryptor.TryReadHeader(filePath);
}
