using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Security;
using Station.Crypto.Kdf;

namespace Station.Infrastructure.Security.Keys;

/// <summary>
/// 主密钥提供者（internal）：
///   - 优先读环境变量 STATION_MASTER_KEY（base64 编码）；
///   - 否则读密钥文件；
///   - 首次启动自动生成。
///   - 支持版本化轮换。
/// </summary>
public sealed class MasterKeyProvider
{
    private const int KeySizeBytes = 32;

    private readonly string _keyFilePath;
    private readonly string _envVarName;
    private readonly ILogger<MasterKeyProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private MasterKeyFile _cache;

    public MasterKeyProvider(
        IOptions<CryptoOptions> options,
        ILogger<MasterKeyProvider> logger)
    {
        var opt = options.Value;
        _keyFilePath = string.IsNullOrWhiteSpace(opt.MasterKeyFile)
            ? GetDefaultKeyPath()
            : opt.MasterKeyFile;
        _envVarName = string.IsNullOrWhiteSpace(opt.EnvOverrideVar)
            ? "STATION_MASTER_KEY"
            : opt.EnvOverrideVar;
        _logger = logger;

        _cache = LoadOrCreate();
    }

    public int CurrentVersion => _cache.Current;

    /// <summary>主密钥文件路径（用于自检和日志展示）。</summary>
    public string KeyFilePath => _keyFilePath;

    /// <summary>当前是否使用环境变量（用于自检提示）。</summary>
    public bool IsUsingEnvironmentVariable =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(_envVarName));

    public byte[] GetKey(int version)
    {
        // 环境变量优先
        var env = Environment.GetEnvironmentVariable(_envVarName);
        if (!string.IsNullOrWhiteSpace(env))
        {
            try { return Convert.FromBase64String(env); }
            catch (FormatException)
            {
                _logger.LogWarning("环境变量 {Var} 不是合法 Base64，回退到密钥文件", _envVarName);
            }
        }

        return _cache.GetKey(version);
    }

    public IReadOnlyList<int> GetVersions() =>
        _cache.Keys.Select(k => k.Version).OrderBy(v => v).ToList();

    /// <summary>生成新版本密钥并设为 current。</summary>
    public async Task<int> RotateAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var newVersion = _cache.Current + 1;
            _cache.Keys.Add(new MasterKeyEntry
            {
                Version = newVersion,
                Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeySizeBytes)),
                CreatedAt = DateTime.UtcNow
            });
            _cache.Current = newVersion;
            await SaveAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("主密钥已轮换到 v{Version}", newVersion);
            return newVersion;
        }
        finally { _lock.Release(); }
    }

    /// <summary>派生 Binding MAC 密钥（HKDF）。</summary>
    public byte[] DeriveBindingKey()
        => HkdfSm3.Derive(GetKey(CurrentVersion), "station:recorder-binding:v1", 32);

    /// <summary>派生文件加密密钥（HKDF，指定主密钥版本）。</summary>
    public byte[] DeriveFileEncryptionKey(int keyVersion)
        => HkdfSm3.Derive(GetKey(keyVersion), "station:file-encryption:v1", 32);

    // ============ 内部 ============

    private MasterKeyFile LoadOrCreate()
    {
        try
        {
            if (File.Exists(_keyFilePath))
            {
                var json = File.ReadAllText(_keyFilePath);
                var file = JsonSerializer.Deserialize<MasterKeyFile>(json, JsonOpts);
                if (file is { Keys.Count: > 0 }) return file;
                _logger.LogWarning("密钥文件为空或格式不正确，将重新生成：{Path}", _keyFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取密钥文件失败，将重新生成：{Path}", _keyFilePath);
        }

        var initial = new MasterKeyFile
        {
            Current = 1,
            Keys =
            {
                new MasterKeyEntry
                {
                    Version = 1,
                    Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeySizeBytes)),
                    CreatedAt = DateTime.UtcNow
                }
            }
        };
        Save(initial);
        _logger.LogInformation("已生成新的主密钥文件：{Path}", _keyFilePath);
        return initial;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_keyFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(_cache, JsonOpts);
        await File.WriteAllTextAsync(_keyFilePath, json, ct).ConfigureAwait(false);
        TryRestrictPermissions(_keyFilePath);
    }

    private void Save(MasterKeyFile file)
    {
        var dir = Path.GetDirectoryName(_keyFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_keyFilePath, JsonSerializer.Serialize(file, JsonOpts));
        TryRestrictPermissions(_keyFilePath);
    }

    private static void TryRestrictPermissions(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var fi = new FileInfo(path);
                var acl = fi.GetAccessControl();
                acl.SetAccessRuleProtection(true, false);
                var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().User;
                if (currentUser is not null)
                {
                    acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                        currentUser,
                        System.Security.AccessControl.FileSystemRights.FullControl,
                        System.Security.AccessControl.AccessControlType.Allow));
                    fi.SetAccessControl(acl);
                }
            }
            else
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch { /* 权限设置失败不阻塞启动 */ }
    }

    private static string GetDefaultKeyPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            return Path.Combine(programData, "Station", "keys", "master.key");
        }
        return "/etc/station/keys/master.key";
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ---- 内部 DTO ----

    private sealed class MasterKeyFile
    {
        [JsonPropertyName("current")] public int Current { get; set; } = 1;
        [JsonPropertyName("keys")] public List<MasterKeyEntry> Keys { get; set; } = new();

        public byte[] GetKey(int version)
        {
            var entry = Keys.FirstOrDefault(k => k.Version == version)
                ?? throw new InvalidOperationException($"密钥版本 v{version} 不存在");
            return Convert.FromBase64String(entry.Key);
        }
    }

    private sealed class MasterKeyEntry
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    }
}
