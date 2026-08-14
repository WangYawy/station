using System.Text.Json;
using System.Text.Json.Nodes;
using Station.Application.Audit;
using Station.Application.Authentication;
using Station.Application.Collecting;
using Station.Application.Licensing;
using Station.Application.PlatformSync;
using Station.Domain.Entities;
using Station.Infrastructure.Security;
using Station.Infrastructure.Storage;

namespace Station.Application.Settings;

/// <summary>
/// 系统设置服务实现：读取 = 配置（appsettings + 运行时覆盖）绑定的选项单例；
/// 修改 = 校验后热应用到选项单例（采集/登录/平台开关即时生效），并整体写回运行时文件保证重启后一致。
/// 存储目标（IStorageTarget 在启动时按 StorageOptions 构建）与 Web 端口/证书需要重启生效。
/// </summary>
public sealed class SystemSettingsService : ISystemSettingsService
{
    private readonly CollectOptions _collect;
    private readonly StorageOptions _storage;
    private readonly AuthOptions _auth;
    private readonly PlatformOptions _platform;
    private readonly StationOptions _station;
    private readonly IRuntimeSettingsFile _runtimeFile;
    private readonly ILicenseService _license;
    private readonly IAuditLogService _audit;

    private static readonly string[] VideoExtensions = [".mp4", ".avi", ".flv", ".mov"];

    private static readonly string[] AncillaryExtensions =
        [".wav", ".mp3", ".aac", ".jpg", ".bmp", ".jpeg", ".png", ".log"];

    public SystemSettingsService(
        CollectOptions collect,
        StorageOptions storage,
        AuthOptions auth,
        PlatformOptions platform,
        StationOptions station,
        IRuntimeSettingsFile runtimeFile,
        ILicenseService license,
        IAuditLogService audit)
    {
        _collect = collect;
        _storage = storage;
        _auth = auth;
        _platform = platform;
        _station = station;
        _runtimeFile = runtimeFile;
        _license = license;
        _audit = audit;
    }

    public async Task<SystemSettingsCoreDto> GetCoreAsync()
    {
        var license = await _license.CheckAsync();
        return new SystemSettingsCoreDto(
            new BasicSettingsDto(
                _storage.StationNo,
                _station.InstallLocation,
                _platform.Enabled ? "platform" : "standalone",
                _platform.BaseUrl ?? string.Empty,
                _platform.StationCode),
            new StorageSettingsDto(
                _storage.Target.ToString(),
                _storage.LocalRoot,
                _storage.DirectoryTemplate,
                _storage.FtpHost,
                _storage.FtpPort,
                _storage.FtpUser ?? string.Empty,
                _storage.SftpHost,
                _storage.SftpPort,
                _storage.SftpUser ?? string.Empty,
                _storage.SftpRoot ?? string.Empty,
                _storage.CircuitBreakerThreshold,
                _storage.CircuitBreakerCooldownSeconds,
                _station.VideoRetentionDays,
                _station.LogRetentionDays,
                $"{_collect.CacheCleanupHour:D2}:{_collect.CacheCleanupMinute:D2}"),
            new CollectSettingsDto(
                _collect.AutoCollectOnConnect,
                _collect.EraseAfterComplete,
                _collect.SkipCollected,
                _collect.FileExtensions.Any(e => AncillaryExtensions.Contains(e, StringComparer.OrdinalIgnoreCase))),
            new LicenseSettingsDto(
                license.Status.ToString(),
                license.Message,
                license.ExpiresAt,
                license.DaysLeft),
            ReadOnly: _platform.Enabled);
    }

    public async Task<IReadOnlyList<string>> UpdateAsync(
        string group,
        IReadOnlyDictionary<string, string> values,
        string operatorAccount)
    {
        var hints = group switch
        {
            "basic" => ApplyBasic(values),
            "storage" => ApplyStorage(values),
            "collect" => ApplyCollect(values),
            _ => throw new ArgumentException($"未知设置分组: {group}")
        };

        PersistRuntime();
        await _audit.WriteAsync(new AuditLog
        {
            OperatorAccount = operatorAccount,
            OperationType = "settings.update",
            Target = group,
            Detail = $"修改设置：{string.Join(", ", values.Keys)}" + (hints.Count > 0 ? $"（{string.Join("；", hints)}）" : string.Empty),
            Result = 1
        });
        return hints;
    }

    // ==================== 分组应用 ====================

    private List<string> ApplyBasic(IReadOnlyDictionary<string, string> values)
    {
        var hints = new List<string>();
        if (values.TryGetValue("stationNo", out var stationNo) && !string.IsNullOrWhiteSpace(stationNo))
        {
            _storage.StationNo = stationNo.Trim();
        }

        if (values.TryGetValue("installLocation", out var location))
        {
            _station.InstallLocation = location.Trim();
        }

        if (values.TryGetValue("runMode", out var runMode) &&
            runMode is "standalone" or "platform")
        {
            var enabled = runMode == "platform";
            if (enabled != _platform.Enabled)
            {
                _platform.Enabled = enabled;
                hints.Add("运行模式切换需重启后完全生效");
            }
        }

        if (values.TryGetValue("platformBaseUrl", out var baseUrl) && !string.IsNullOrWhiteSpace(baseUrl))
        {
            _platform.BaseUrl = baseUrl.Trim();
            hints.Add("平台地址修改需重启后生效");
        }

        if (values.TryGetValue("platformStationCode", out var platformCode) && !string.IsNullOrWhiteSpace(platformCode))
        {
            _platform.StationCode = platformCode.Trim();
            hints.Add("平台采集站编号修改需重启后生效");
        }

        return hints;
    }

    private List<string> ApplyStorage(IReadOnlyDictionary<string, string> values)
    {
        var hints = new List<string> { "存储目标/连接参数修改需重启后生效" };
        if (values.TryGetValue("target", out var target) &&
            Enum.TryParse<StorageTargetKind>(target, true, out var targetKind))
        {
            _storage.Target = targetKind;
        }

        if (values.TryGetValue("localRoot", out var localRoot))
        {
            _storage.LocalRoot = localRoot.Trim();
        }

        if (values.TryGetValue("directoryTemplate", out var template) && !string.IsNullOrWhiteSpace(template))
        {
            _storage.DirectoryTemplate = template.Trim();
        }

        if (values.TryGetValue("ftpHost", out var ftpHost)) _storage.FtpHost = ftpHost.Trim();
        if (values.TryGetValue("ftpPort", out var ftpPort) && int.TryParse(ftpPort, out var fp)) _storage.FtpPort = fp;
        if (values.TryGetValue("ftpUser", out var ftpUser)) _storage.FtpUser = ftpUser.Trim();
        if (values.TryGetValue("ftpPassword", out var ftpPassword) && !string.IsNullOrWhiteSpace(ftpPassword))
        {
            _storage.FtpPassword = Sm4SecretProtector.Protect(ftpPassword);
        }

        if (values.TryGetValue("sftpHost", out var sftpHost)) _storage.SftpHost = sftpHost.Trim();
        if (values.TryGetValue("sftpPort", out var sftpPort) && int.TryParse(sftpPort, out var sp)) _storage.SftpPort = sp;
        if (values.TryGetValue("sftpUser", out var sftpUser)) _storage.SftpUser = sftpUser.Trim();
        if (values.TryGetValue("sftpPassword", out var sftpPassword) && !string.IsNullOrWhiteSpace(sftpPassword))
        {
            _storage.SftpPassword = Sm4SecretProtector.Protect(sftpPassword);
        }

        if (values.TryGetValue("sftpRoot", out var sftpRoot)) _storage.SftpRoot = sftpRoot.Trim();
        if (values.TryGetValue("circuitBreakerThreshold", out var threshold) && int.TryParse(threshold, out var t))
        {
            _storage.CircuitBreakerThreshold = Math.Max(1, t);
        }

        if (values.TryGetValue("circuitBreakerCooldownSeconds", out var cooldown) && int.TryParse(cooldown, out var c))
        {
            _storage.CircuitBreakerCooldownSeconds = Math.Max(1, c);
        }

        if (values.TryGetValue("videoRetentionDays", out var videoDays) && int.TryParse(videoDays, out var vd))
        {
            _station.VideoRetentionDays = Math.Max(1, vd);
            _collect.CacheRetentionDays = Math.Max(1, vd);
        }

        if (values.TryGetValue("logRetentionDays", out var logDays) && int.TryParse(logDays, out var ld))
        {
            _station.LogRetentionDays = Math.Max(1, ld);
        }

        if (values.TryGetValue("cleanupTime", out var cleanupTime) &&
            TimeSpan.TryParse(cleanupTime, out var time))
        {
            _collect.CacheCleanupHour = time.Hours;
            _collect.CacheCleanupMinute = time.Minutes;
        }

        return hints;
    }

    private List<string> ApplyCollect(IReadOnlyDictionary<string, string> values)
    {
        if (values.TryGetValue("autoCollectOnConnect", out var auto))
        {
            _collect.AutoCollectOnConnect = ParseBool(auto, _collect.AutoCollectOnConnect);
        }

        if (values.TryGetValue("eraseAfterComplete", out var erase))
        {
            _collect.EraseAfterComplete = ParseBool(erase, _collect.EraseAfterComplete);
        }

        if (values.TryGetValue("skipCollected", out var skip))
        {
            _collect.SkipCollected = ParseBool(skip, _collect.SkipCollected);
        }

        if (values.TryGetValue("collectAncillaryFiles", out var ancillary))
        {
            var enabled = ParseBool(ancillary, true);
            _collect.FileExtensions = enabled
                ? VideoExtensions.Concat(AncillaryExtensions).ToList()
                : VideoExtensions.ToList();
        }

        return [];
    }

    // ==================== 运行时文件持久化 ====================

    private void PersistRuntime()
    {
        var root = LoadRuntime();
        var station = EnsureObject(root, "Station");
        station["Basic"] = JsonSerializer.SerializeToNode(new
        {
            _station.InstallLocation,
            _station.VideoRetentionDays,
            _station.LogRetentionDays
        });
        station["Storage"] = JsonSerializer.SerializeToNode(new
        {
            Target = _storage.Target.ToString(),
            _storage.LocalRoot,
            _storage.DirectoryTemplate,
            _storage.FtpHost,
            _storage.FtpPort,
            _storage.FtpUser,
            FtpPassword = _storage.FtpPassword,
            _storage.SftpHost,
            _storage.SftpPort,
            _storage.SftpUser,
            SftpPassword = _storage.SftpPassword,
            _storage.SftpRoot,
            _storage.CircuitBreakerThreshold,
            _storage.CircuitBreakerCooldownSeconds
        });
        station["Collect"] = JsonSerializer.SerializeToNode(new
        {
            _collect.AutoCollectOnConnect,
            _collect.EraseAfterComplete,
            _collect.SkipCollected,
            _collect.FileExtensions,
            _collect.CacheRetentionDays,
            _collect.CacheCleanupHour,
            _collect.CacheCleanupMinute
        });
        station["Auth"] = JsonSerializer.SerializeToNode(new
        {
            _auth.MaxFailedAttempts,
            _auth.LockoutMinutes
        });
        station["Platform"] = JsonSerializer.SerializeToNode(new
        {
            _platform.Enabled,
            _platform.BaseUrl,
            _platform.StationCode
        });
        _runtimeFile.WriteJson(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private JsonObject LoadRuntime()
    {
        try
        {
            var json = _runtimeFile.ReadJson();
            return string.IsNullOrWhiteSpace(json)
                ? new JsonObject()
                : (JsonNode.Parse(json) as JsonObject) ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static bool ParseBool(string value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static JsonObject EnsureObject(JsonObject parent, string key)
    {
        if (parent[key] is not JsonObject child)
        {
            child = new JsonObject();
            parent[key] = child;
        }

        return child;
    }
}
