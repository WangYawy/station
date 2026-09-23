using Station.Application.Security.Models;

namespace Station.Application.Security.Abstractions;

/// <summary>
/// 加密策略服务：读写 station_crypto_policy，进程内缓存，变更事件。
/// 热路径（登录校验等）应优先调用本服务，避免直接查库。
/// </summary>
public interface ICryptoPolicyService
{
    /// <summary>按用途获取策略（命中缓存）。</summary>
    Task<CryptoPolicySnapshot> GetAsync(string usageCode, CancellationToken ct = default);

    /// <summary>获取全部策略。</summary>
    Task<IReadOnlyList<CryptoPolicySnapshot>> GetAllAsync(CancellationToken ct = default);

    /// <summary>更新策略（写库 + 清缓存 + 触发事件）。</summary>
    Task UpdateAsync(string usageCode, CryptoPolicyUpdate update,
                     string operatorAccount, CancellationToken ct = default);

    /// <summary>策略变更事件（用于 UI 刷新、授权/文件签名模块失效缓存）。</summary>
    event EventHandler<CryptoPolicyChangedEventArgs>? Changed;

    /// <summary>手动刷新缓存（如平台下发后）。</summary>
    void InvalidateCache(string? usageCode = null);
}
