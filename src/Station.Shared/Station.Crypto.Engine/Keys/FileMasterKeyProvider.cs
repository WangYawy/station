using System.Security.Cryptography;
using Station.Crypto;
using Station.Crypto.Engine.Kdf;

namespace Station.Crypto.Engine.Keys;

/// <summary>
/// 文件主密钥提供者（单机版）。
///
/// 【加载优先级】
///   1. 环境变量（若指定且存在）— 完全覆盖密钥文件，忽略版本号
///   2. 密钥文件 — 按版本号读取
///
/// 【线程安全】
///   - GetKey / CurrentVersion / GetVersions：用 Volatile.Read 读 _cache 引用；
///     _cache 指向不可变 DTO，替换引用而非原地修改，无需加锁。
///   - RotateAsync：用 SemaphoreSlim 串行化，先落盘成功再更新内存。
///
/// 【为什么不用 lock】
///   加解密高频调用 GetKey，锁会成为热点。
///   引用替换 + 不可变对象是并发读写的经典无锁模式。
/// </summary>
public sealed class FileMasterKeyProvider : IMasterKeyProvider
{
    private const int KeySizeBytes = 32;

    private readonly string _filePath;
    private readonly Lazy<byte[]?> _envKeyLazy;
    private readonly SemaphoreSlim _rotateLock = new(1, 1);

    /// <summary>内存缓存（不可变，替换引用而非原地修改）。</summary>
    private MasterKeyFileDto _cache;

    /// <summary>
    /// 构造。
    /// </summary>
    /// <param name="filePath">主密钥文件路径（绝对路径或相对路径均可）。</param>
    /// <param name="envVarName">
    /// 环境变量名（可选）。若指定且环境变量存在，则完全覆盖文件。
    /// 环境变量值应为 Base64 编码的 32 字节密钥。
    /// </param>
    public FileMasterKeyProvider(string filePath, string? envVarName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;

        _envKeyLazy = new Lazy<byte[]?>(
            () => ResolveEnvKey(envVarName),
            LazyThreadSafetyMode.ExecutionAndPublication);

        _cache = MasterKeyFileStore.LoadOrCreate(filePath);
    }

    /// <summary>主密钥文件路径（诊断 / 日志用）。</summary>
    public string FilePath => _filePath;

    /// <summary>是否正在使用环境变量覆盖（SelfCheck 用）。</summary>
    public bool IsUsingEnvironmentVariable => _envKeyLazy.Value is not null;

    public int CurrentVersion
    {
        get
        {
            // 环境变量模式下只有一个逻辑版本
            if (_envKeyLazy.Value is not null) return 1;
            return Volatile.Read(ref _cache).Current;
        }
    }

    public IReadOnlyList<int> GetVersions()
    {
        if (_envKeyLazy.Value is not null)
            return new[] { 1 };

        var cache = Volatile.Read(ref _cache);
        var versions = new int[cache.Keys.Count];
        for (var i = 0; i < cache.Keys.Count; i++)
            versions[i] = cache.Keys[i].Version;
        Array.Sort(versions);
        return versions;
    }

    public byte[] GetKey(int version)
    {
        // 环境变量优先（返回副本，防止调用方修改）
        var envKey = _envKeyLazy.Value;
        if (envKey is not null) return (byte[])envKey.Clone();

        // 文件模式：MasterKeyFileDto.GetKey 每次返回新数组
        var cache = Volatile.Read(ref _cache);
        return cache.GetKey(version);
    }

    public async Task<int> RotateAsync(CancellationToken ct = default)
    {
        if (_envKeyLazy.Value is not null)
            throw new CryptoException(
                "环境变量模式不支持密钥轮换（环境变量无法持久化新密钥）");

        await _rotateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var old = Volatile.Read(ref _cache);
            var newVersion = old.Current + 1;

            var newEntry = new MasterKeyEntryDto
            {
                Version = newVersion,
                Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeySizeBytes)),
                CreatedAt = DateTime.UtcNow
            };

            var newKeys = new List<MasterKeyEntryDto>(old.Keys) { newEntry };
            var newDto = old with { Current = newVersion, Keys = newKeys };

            // 先落盘，成功后再更新内存引用 —— 保证崩溃一致性
            await MasterKeyFileStore.SaveAsync(_filePath, newDto, ct).ConfigureAwait(false);
            Volatile.Write(ref _cache, newDto);

            return newVersion;
        }
        finally
        {
            _rotateLock.Release();
        }
    }

    public byte[] DeriveBindingKey()
        => HkdfSm3.Derive(GetKey(CurrentVersion), HkdfInfo.RecorderBinding, 32);

    public byte[] DeriveFileEncryptionKey(int keyVersion)
        => HkdfSm3.Derive(GetKey(keyVersion), HkdfInfo.FileEncryption, 32);

    // ================================================================
    // 内部
    // ================================================================

    /// <summary>
    /// 解析环境变量。非法（格式错误 / 长度不对）返回 null，回退文件。
    /// 不抛异常：环境变量可能是误设的，静默回退更友好。
    /// </summary>
    private static byte[]? ResolveEnvKey(string? envVarName)
    {
        if (string.IsNullOrEmpty(envVarName)) return null;

        var value = Environment.GetEnvironmentVariable(envVarName);
        if (string.IsNullOrWhiteSpace(value)) return null;

        try
        {
            var key = Convert.FromBase64String(value);
            return key.Length == KeySizeBytes ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
