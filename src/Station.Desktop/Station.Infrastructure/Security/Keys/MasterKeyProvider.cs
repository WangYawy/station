using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Station.Application.Security.Abstractions;
using Station.Infrastructure.Security.Policy;

namespace Station.Infrastructure.Security.Keys;

/// <summary>
/// 主密钥提供者：
/// - 优先使用环境变量 STATION_MASTER_KEY（值为 base64(32 字节)）；
/// - 否则从密钥文件读取；
/// - 密钥文件不存在时自动生成（首次启动）。
/// 
/// 密钥文件格式（JSON）：
/// {
///   "current": 2,
///   "keys": [
///     { "version": 1, "key": "base64", "createdAt": "..." },
///     { "version": 2, "key": "base64", "createdAt": "..." }
///   ]
/// }
/// 
/// 默认路径：
/// - Windows: %ProgramData%\Station\keys\master.key
/// - Linux:   /etc/station/keys/master.key
/// </summary>
public sealed class MasterKeyProvider : IMasterKeyProvider
{
    private const int KeySizeBytes = 32;
    private readonly string _keyFilePath;
    private readonly string _envVarName;
    private readonly ILogger<MasterKeyProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private MasterKeyFile _cache = new();

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

    public byte[] GetKey(int version)
    {
        // 环境变量优先（适用于容器/K8s 场景）
        var env = Environment.GetEnvironmentVariable(_envVarName);
        if (!string.IsNullOrWhiteSpace(env))
        {
            try { return Convert.FromBase64String(env); }
            catch (FormatException)
            {
                _logger.LogWarning("环境变量 {Var} 不是合法 Base64，回退到密钥文件", _envVarName);
            }
        }

        var entry = _cache.Keys.FirstOrDefault(k => k.Version == version);
        if (entry is null)
            throw new InvalidOperationException($"密钥版本 v{version} 不存在");
        return Convert.FromBase64String(entry.Key);
    }

    public IReadOnlyList<int> GetVersions() =>
        _cache.Keys.Select(k => k.Version).OrderBy(v => v).ToList();

    public async Task<int> RotateAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var newVersion = _cache.Current + 1;
            var newKey = RandomNumberGenerator.GetBytes(KeySizeBytes);
            _cache.Keys.Add(new MasterKeyEntry
            {
                Version = newVersion,
                Key = Convert.ToBase64String(newKey),
                CreatedAt = DateTime.UtcNow
            });
            _cache.Current = newVersion;
            await SaveAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("主密钥已轮换到 v{Version}", newVersion);
            return newVersion;
        }
        finally { _lock.Release(); }
    }

    private MasterKeyFile LoadOrCreate()
    {
        try
        {
            if (File.Exists(_keyFilePath))
            {
                var json = File.ReadAllText(_keyFilePath);
                var file = JsonSerializer.Deserialize<MasterKeyFile>(json);
                if (file is { Keys.Count: > 0 }) return file;
                _logger.LogWarning("密钥文件为空或格式不正确，将重新生成：{Path}", _keyFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取密钥文件失败，将重新生成：{Path}", _keyFilePath);
        }

        // 首次生成
        var initial = new MasterKeyFile
        {
            Current = 1,
            Keys = new List<MasterKeyEntry>
            {
                new()
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

    /// <summary>限制密钥文件访问权限（Windows 用 ACL，Linux 用 chmod 600）。</summary>
    private static void TryRestrictPermissions(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var fi = new FileInfo(path);
                var acl = fi.GetAccessControl();
                acl.SetAccessRuleProtection(true, false);
                // 仅当前用户可读写
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
        catch
        {
            // 权限设置失败不应阻塞启动；由部署方负责加固
        }
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
    }

    private sealed class MasterKeyEntry
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("key")] public string Key { get; set; } = string.Empty;
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    }
}
