using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Station.Application.Security.Abstractions;
using Station.Application.Security.Models;
using Station.Domain.Entities;
using Station.Domain.Repositories;
using Station.Domain.Security;
using Vanara.Extensions.Reflection;

namespace Station.Infrastructure.Security.Policy;

/// <summary>
/// 策略服务：读 station_crypto_policy，进程内缓存（滑动 30 分钟），
/// 变更时清缓存并广播 Changed 事件。
/// 
/// 【LegacyAlgorithms 处理】
///   - DB 的 LegacyAlgorithmsJson 列若存在，反序列化为列表；
///   - 若列为空或反序列化失败，回退到 CryptoDefaults.LegacyAlgorithms；
///   - 若 CryptoDefaults 也无该用途定义，返回空列表。
/// </summary>
public sealed class CryptoPolicyService : ICryptoPolicyService
{
    private const string CacheKeyPrefix = "crypto-policy:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IRepository<SysCryptoPolicy> _policys;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CryptoPolicyService> _logger;

    public event EventHandler<CryptoPolicyChangedEventArgs>? Changed;

    public CryptoPolicyService(
        IRepository<SysCryptoPolicy> policys,
        IMemoryCache cache,
        ILogger<CryptoPolicyService> logger)
    {
        _policys = policys;
        _cache = cache;
        _logger = logger;
    }

    #region // ========== 读取 ==========

    public async Task<CryptoPolicySnapshot> GetAsync(string usageCode, CancellationToken ct = default)
    {
        var key = CacheKeyPrefix + usageCode;
        if (_cache.TryGetValue(key, out CryptoPolicySnapshot? cached) && cached is not null)
            return cached;

        var entity = await _policys.FirstAsync(p => p.UsageCode == usageCode && p.Enabled, ct).ConfigureAwait(false);

        CryptoPolicySnapshot snap;
        if (entity is null)
        {
            // 兜底：用出厂默认
            snap = BuildDefaultSnapshot(usageCode);
        }
        else
        {
            snap = ToSnapshot(entity);
        }

        _cache.Set(key, snap, new MemoryCacheEntryOptions
        {
            SlidingExpiration = CacheTtl,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2)
        });
        return snap;
    }

    public async Task<IReadOnlyList<CryptoPolicySnapshot>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _policys.GetListAsync(p => p.Enabled, ct).ConfigureAwait(false);

        var result = new List<CryptoPolicySnapshot>(list.Count);
        foreach (var e in list)
        {
            var snap = ToSnapshot(e);
            _cache.Set(CacheKeyPrefix + e.UsageCode, snap, CacheTtl);
            result.Add(snap);
        }

        // 补齐出厂默认中缺失的用途
        foreach (var (usage, algo) in CryptoDefaults.Primary)
        {
            if (result.All(r => !string.Equals(r.UsageCode, usage, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(BuildDefaultSnapshot(usage));
            }
        }
        return result;
    }
    #endregion

    #region // ========== 更新 ==========

    public async Task UpdateAsync(
        string usageCode,
        CryptoPolicyUpdate update,
        string operatorAccount,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(update.Algorithm))
            throw new ArgumentException("算法不能为空", nameof(update));

        var existing = await _policys.FirstAsync(p => p.UsageCode == usageCode, ct).ConfigureAwait(false);

        var oldAlgo = existing?.Algorithm ?? string.Empty;

        if (existing is null)
        {
            existing = new SysCryptoPolicy
            {
                UsageCode = usageCode,
                Algorithm = update.Algorithm,
                SecondaryAlgorithm = update.SecondaryAlgorithm,
                ParamsJson = update.ParamsJson,
                LegacyAlgorithmsJson = null,     // 走 CryptoDefaults 默认
                AllowLegacy = update.AllowLegacy ?? CryptoDefaults.DefaultAllowLegacy,
                Enabled = update.Enabled ?? true,
                Source = "Local",
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = operatorAccount
            };
            await _policys.InsertAsync(existing, ct).ConfigureAwait(false);
        }
        else
        {
            existing.Algorithm = update.Algorithm;
            if (update.SecondaryAlgorithm is not null) existing.SecondaryAlgorithm = update.SecondaryAlgorithm;
            if (update.ParamsJson is not null) existing.ParamsJson = update.ParamsJson;
            if (update.AllowLegacy.HasValue) existing.AllowLegacy = update.AllowLegacy.Value;
            if (update.Enabled.HasValue) existing.Enabled = update.Enabled.Value;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = operatorAccount;
            await _policys.UpdateAsync(existing, ct).ConfigureAwait(false);
        }

        InvalidateCache(usageCode);
        _logger.LogInformation("加密策略已更新：{Usage} {Old} → {New} by {Op}",
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
            // 全量失效：清空所有可能的缓存 key（简单做法：遍历已知用途）
            foreach (var usage in CryptoDefaults.Primary.Keys)
                _cache.Remove(CacheKeyPrefix + usage);
        }
        else
        {
            _cache.Remove(CacheKeyPrefix + usageCode);
        }
    }
    #endregion

    #region // ========== 映射与反序列化 ========== 

    private static CryptoPolicySnapshot ToSnapshot(SysCryptoPolicy e) =>
        new(
            UsageCode: e.UsageCode,
            Algorithm: e.Algorithm,
            SecondaryAlgorithm: e.SecondaryAlgorithm,
            ParamsJson: e.ParamsJson,
            AllowLegacy: e.AllowLegacy,
            LegacyAlgorithms: ParseLegacy(e.LegacyAlgorithmsJson, e.UsageCode),
            Enabled: e.Enabled,
            Source: e.Source,
            UpdatedAt: e.UpdatedAt,
            UpdatedBy: e.UpdatedBy);

    /// <summary>
    /// 反序列化 LegacyAlgorithmsJson。
    /// 优先级：DB JSON > CryptoDefaults > 空列表。
    /// </summary>
    private static IReadOnlyList<string> ParseLegacy(string? json, string usageCode)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(json, JsonOpts);
                if (list is { Count: > 0 })
                    return list;
            }
            catch (JsonException)
            {
                // JSON 损坏 → 回退默认
            }
        }
        return CryptoDefaults.GetDefaultLegacy(usageCode);
    }

    /// <summary>构造 DB 无记录时的兜底快照。</summary>
    private static CryptoPolicySnapshot BuildDefaultSnapshot(string usageCode)
    {
        if (!CryptoDefaults.Primary.TryGetValue(usageCode, out var algo))
            throw new NotSupportedException($"未知用途：{usageCode}");

        CryptoDefaults.Secondary.TryGetValue(usageCode, out var secondary);
        return new CryptoPolicySnapshot(
            UsageCode: usageCode,
            Algorithm: algo,
            SecondaryAlgorithm: secondary,
            ParamsJson: null,
            AllowLegacy: CryptoDefaults.DefaultAllowLegacy,
            LegacyAlgorithms: CryptoDefaults.GetDefaultLegacy(usageCode),
            Enabled: true,
            Source: "Local",
            UpdatedAt: DateTime.UtcNow,
            UpdatedBy: "system");
    }
    #endregion
}
