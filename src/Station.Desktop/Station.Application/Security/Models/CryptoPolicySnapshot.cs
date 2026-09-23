namespace Station.Application.Security.Models;

/// <summary>
/// 加密策略快照（只读）。业务代码通过 ICryptoPolicyService.GetAsync 获得。
/// </summary>
/// <param name="UsageCode">用途标识（password/license/file_sig/secret_field）。</param>
/// <param name="Algorithm">主算法标识。</param>
/// <param name="SecondaryAlgorithm">组合算法第二段（如 License 的签名算法）。</param>
/// <param name="ParamsJson">算法参数（JSON），生产环境参数走默认，此处仅保留扩展位。</param>
/// <param name="AllowLegacy">是否允许迁移期旧算法回退。</param>
/// <param name="LegacyAlgorithms">
/// 迁移期允许回退的旧算法列表（按优先级排序）。
/// 从 DB 的 LegacyAlgorithmsJson 列反序列化；为空时使用 CryptoDefaults 默认值。
/// </param>
/// <param name="Enabled">是否启用。</param>
/// <param name="Source">策略来源：Local（单机自治）/ Platform（平台下发）。</param>
/// <param name="UpdatedAt">最后更新时间（UTC）。</param>
/// <param name="UpdatedBy">最后更新人。</param>
public sealed record CryptoPolicySnapshot(
    string UsageCode,
    string Algorithm,
    string? SecondaryAlgorithm,
    string? ParamsJson,
    bool AllowLegacy,
    IReadOnlyList<string> LegacyAlgorithms,
    bool Enabled,
    string Source,
    DateTime UpdatedAt,
    string? UpdatedBy);
