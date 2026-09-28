using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Security;
using Station.Crypto;
using Station.Crypto.Engine.Keys;

namespace Station.Infrastructure.Security.Keys;

/// <summary>
/// 主密钥提供者（DI 注册用壳）。
///
/// 【职责】根据 CryptoOptions.Mode 选择具体实现：
///   - "Local" → FileMasterKeyProvider（读本地文件）
///   - "Remote" → RemoteMasterKeyProvider（平台下发，暂未实现）
///
/// 【为什么保留这个壳】
///   1. 对上层暴露 IMasterKeyProvider 契约，隐藏实现细节
///   2. SelfCheck 需要 KeyFilePath / IsUsingEnvironmentVariable 等诊断属性
/// </summary>
public sealed class MasterKeyProvider : IMasterKeyProvider
{
    private readonly IMasterKeyProvider _inner;

    public MasterKeyProvider(
        IOptions<CryptoOptions> options,
        ILogger<MasterKeyProvider> logger)
    {
        var opt = options.Value;
        var mode = string.IsNullOrWhiteSpace(opt.Mode) ? "Local" : opt.Mode.Trim();

        _inner = mode.ToLowerInvariant() switch
        {
            "local" => CreateLocal(opt),
            "remote" => new RemoteMasterKeyProvider(options, logger),
            _ => throw new NotSupportedException($"未知的主密钥模式：{mode}")
        };

        logger.LogInformation("主密钥模式：{Mode}", mode);
    }

    /// <summary>主密钥文件路径（Mode=Remote 时返回 "(remote)"）。</summary>
    public string KeyFilePath =>
        (_inner as FileMasterKeyProvider)?.FilePath ?? "(remote)";

    /// <summary>是否正在使用环境变量覆盖。</summary>
    public bool IsUsingEnvironmentVariable =>
        (_inner as FileMasterKeyProvider)?.IsUsingEnvironmentVariable ?? false;

    // ===== IMasterKeyProvider 委托 =====

    public int CurrentVersion => _inner.CurrentVersion;
    public IReadOnlyList<int> GetVersions() => _inner.GetVersions();
    public byte[] GetKey(int version) => _inner.GetKey(version);
    public Task<int> RotateAsync(CancellationToken ct = default) => _inner.RotateAsync(ct);
    public byte[] DeriveBindingKey() => _inner.DeriveBindingKey();
    public byte[] DeriveFileEncryptionKey(int keyVersion)
        => _inner.DeriveFileEncryptionKey(keyVersion);

    // ===== 内部 =====

    private static FileMasterKeyProvider CreateLocal(CryptoOptions opt)
    {
        var path = string.IsNullOrWhiteSpace(opt.MasterKeyFile)
            ? GetDefaultKeyPath()
            : opt.MasterKeyFile;

        var envVar = string.IsNullOrWhiteSpace(opt.EnvOverrideVar)
            ? "STATION_MASTER_KEY"
            : opt.EnvOverrideVar;

        return new FileMasterKeyProvider(path, envVar);
    }

    private static string GetDefaultKeyPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var programData = Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, "Station", "keys", "master.key");
        }
        return "/etc/station/keys/master.key";
    }
}
