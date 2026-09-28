using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Station.Application.Security;
using Station.Crypto;
using Station.Domain.Entities;
using Station.Infrastructure.Settings;

namespace Station.Infrastructure.Security;

/// <summary>
/// 加密策略服务实现：策略读写 + 算法工厂 + 密钥轮换。
/// </summary>
public sealed class CryptoPolicyService : ICryptoPolicyService
{
    private const string CacheKeyPrefix = "crypto-policy:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    private readonly ISqlSugarClient _db;
    private readonly IMemoryCache _cache;
    private readonly IMasterKeyProvider _keys;
    private readonly ICryptoAlgorithmRegistry _registry;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CryptoPolicyService> _logger;

    public event EventHandler<CryptoPolicyChangedEventArgs>? Changed;

    public CryptoPolicyService(
        ISqlSugarClient db,
        IMemoryCache cache,
        IMasterKeyProvider keys,
        ICryptoAlgorithmRegistry registry,
        IServiceProvider serviceProvider,
        ILogger<CryptoPolicyService> logger)
    {
        _db = db;
        _cache = cache;
        _keys = keys;
        _registry = registry;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    // 策略读取
    public async Task<CryptoPolicySnapshot> GetAsync(string usageCode, CancellationToken ct = default)
    {
        var key = CacheKeyPrefix + usageCode;
        if (_cache.TryGetValue(key, out CryptoPolicySnapshot? cached) && cached is not null)
            return cached;

        var entity = await _db.Queryable<SysCryptoPolicy>()
            .Where(p => p.UsageCode == usageCode && p.Enabled)
            .FirstAsync(ct).ConfigureAwait(false);

        var snap = entity is null ? BuildDefault(usageCode) : ToSnapshot(entity);

        _cache.Set(key, snap, new MemoryCacheEntryOptions
        {
            SlidingExpiration = CacheTtl,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2)
        });
        return snap;
    }

    public async Task<IReadOnlyList<CryptoPolicySnapshot>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _db.Queryable<SysCryptoPolicy>()
            .Where(p => p.Enabled)
            .ToListAsync(ct).ConfigureAwait(false);

        var result = new List<CryptoPolicySnapshot>(list.Count);
        foreach (var e in list)
        {
            var snap = ToSnapshot(e);
            _cache.Set(CacheKeyPrefix + e.UsageCode, snap, CacheTtl);
            result.Add(snap);
        }

        foreach (var (usage, _) in CryptoDefaults.Primary)
        {
            if (result.All(r => !string.Equals(r.UsageCode, usage, StringComparison.OrdinalIgnoreCase)))
                result.Add(BuildDefault(usage));
        }
        return result;
    }

    #region // 策略更新
    public async Task UpdateAsync(
        string usageCode, CryptoPolicyUpdate update,
        string operatorAccount, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(update.Algorithm))
            throw new ArgumentException("算法不能为空", nameof(update));

        var existing = await _db.Queryable<SysCryptoPolicy>()
            .Where(p => p.UsageCode == usageCode)
            .FirstAsync(ct).ConfigureAwait(false);

        var oldAlgo = existing?.Algorithm ?? string.Empty;

        if (existing is null)
        {
            await _db.Insertable(new SysCryptoPolicy
            {
                UsageCode = usageCode,
                Algorithm = update.Algorithm,
                SecondaryAlgorithm = update.SecondaryAlgorithm,
                AllowLegacy = update.AllowLegacy ?? CryptoDefaults.DefaultAllowLegacy,
                Enabled = update.Enabled ?? true,
                Source = "Local",
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = operatorAccount
            }).ExecuteCommandAsync(ct).ConfigureAwait(false);
        }
        else
        {
            existing.Algorithm = update.Algorithm;
            if (update.SecondaryAlgorithm is not null) existing.SecondaryAlgorithm = update.SecondaryAlgorithm;
            if (update.AllowLegacy.HasValue) existing.AllowLegacy = update.AllowLegacy.Value;
            if (update.Enabled.HasValue) existing.Enabled = update.Enabled.Value;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = operatorAccount;
            await _db.Updateable(existing).ExecuteCommandAsync(ct).ConfigureAwait(false);
        }

        InvalidateCache(usageCode);
        _logger.LogInformation("加密策略更新：{Usage} {Old} → {New} by {Op}",
            usageCode, oldAlgo, update.Algorithm, operatorAccount);

        Changed?.Invoke(this, new CryptoPolicyChangedEventArgs
        {
            UsageCode = usageCode,
            OldAlgorithm = oldAlgo,
            NewAlgorithm = update.Algorithm,
            OperatorAccount = operatorAccount
        });
    }

    public void InvalidateCache(string? usageCode = null)
    {
        if (usageCode is null)
        {
            foreach (var usage in CryptoDefaults.Primary.Keys)
                _cache.Remove(CacheKeyPrefix + usage);
        }
        else
        {
            _cache.Remove(CacheKeyPrefix + usageCode);
        }
    }
    #endregion

    #region // 密钥轮换

    public async Task<KeyRotationResult> RotateMasterKeyAsync(
        string operatorAccount, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var oldVersion = _keys.CurrentVersion;
        var newVersion = await _keys.RotateAsync(ct).ConfigureAwait(false);

        using var scope = _serviceProvider.CreateScope();
        var settingStore = scope.ServiceProvider.GetRequiredService<ISettingStore>();

        var allSettings = await settingStore.GetAllAsync(ct).ConfigureAwait(false);
        var encrypted = allSettings.Where(s => s.IsEncrypted).ToList();

        var ok = 0;
        var fail = 0;
        foreach (var row in encrypted)
        {
            try
            {
                var plain = await settingStore.GetRawAsync(row.GroupKey, row.SubKey, ct)
                    .ConfigureAwait(false);
                if (plain is null) { fail++; continue; }

                await settingStore.SetRawAsync(
                    row.GroupKey, row.SubKey, plain, secret: true, operatorAccount, ct)
                    .ConfigureAwait(false);
                ok++;
            }
            catch (Exception ex)
            {
                fail++;
                _logger.LogError(ex, "重加密失败：{Group}.{Key}", row.GroupKey, row.SubKey);
            }
        }

        sw.Stop();
        _logger.LogInformation("主密钥轮换完成：v{Old}→v{New}，重加密 {Ok}/{Total}",
            oldVersion, newVersion, ok, ok + fail);

        return new KeyRotationResult(oldVersion, newVersion, ok, fail, sw.Elapsed);
    }

    public int GetCurrentKeyVersion() => _keys.CurrentVersion;
    #endregion

    #region // 算法获取
    public async Task<IPasswordHasher> GetPasswordHasherAsync(CancellationToken ct = default)
    {
        var policy = await GetAsync(CryptoUsage.Password, ct).ConfigureAwait(false);
        return _registry.GetPasswordHasher(policy.Algorithm);
    }

    public async Task<ISigner> GetSignerAsync(string usageCode, CancellationToken ct = default)
    {
        var policy = await GetAsync(usageCode, ct).ConfigureAwait(false);
        var algo = policy.SecondaryAlgorithm ?? policy.Algorithm;
        return _registry.GetSigner(algo);
    }

    public async Task<IEncryptor> GetEncryptorAsync(string usageCode, CancellationToken ct = default)
    {
        var policy = await GetAsync(usageCode, ct).ConfigureAwait(false);
        return _registry.GetEncryptor(policy.Algorithm, _keys);
    }

    public async Task<IHasher> GetHasherAsync(string usageCode, CancellationToken ct = default)
    {
        var policy = await GetAsync(usageCode, ct).ConfigureAwait(false);
        return _registry.GetHasher(policy.Algorithm);
    }

    public async Task<IMacProvider> GetMacProviderAsync(string usageCode, CancellationToken ct = default)
    {
        var policy = await GetAsync(usageCode, ct).ConfigureAwait(false);
        return _registry.GetMacProvider(policy.Algorithm);
    }
    #endregion

    public Task<byte[]> GetBindingMacKeyAsync(CancellationToken ct = default)
    {
        var key = _keys.DeriveBindingKey();
        return Task.FromResult(key);
    }

    // =========================================================
    // 内部
    // =========================================================

    private static CryptoPolicySnapshot ToSnapshot(SysCryptoPolicy e) =>
        new(e.UsageCode, e.Algorithm, e.SecondaryAlgorithm,
            e.AllowLegacy, ParseLegacy(e.LegacyAlgorithmsJson, e.UsageCode),
            e.Enabled, e.Source, e.UpdatedAt, e.UpdatedBy);

    private static IReadOnlyList<string> ParseLegacy(string? json, string usageCode)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                if (list is { Count: > 0 }) return list;
            }
            catch { /* 回退默认 */ }
        }
        return CryptoDefaults.GetDefaultLegacy(usageCode);
    }

    private static CryptoPolicySnapshot BuildDefault(string usageCode)
    {
        if (!CryptoDefaults.Primary.TryGetValue(usageCode, out var algo))
            throw new NotSupportedException($"未知用途：{usageCode}");

        CryptoDefaults.Secondary.TryGetValue(usageCode, out var secondary);
        return new CryptoPolicySnapshot(
            usageCode, algo, secondary,
            CryptoDefaults.DefaultAllowLegacy,
            CryptoDefaults.GetDefaultLegacy(usageCode),
            true, "Local", DateTime.UtcNow, "system");
    }
}
