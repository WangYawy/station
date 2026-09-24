namespace Station.Application.Security;

/// <summary>
/// 加密 AAD（附加认证数据）常量与派生规则。
/// 
/// ⚠️ 重要契约说明
/// ────────────────────────────────────────────────────────────
/// 本文件中所有【字符串常量】都是【持久化契约】：
///   - 一旦上线，字符串值【禁止修改】；
///   - 任何字符变更（包括大小写、点、下划线）会导致对应的历史密文永久无法解密；
///   - 如需变更，必须走数据迁移流程（旧密文解密 → 用新 AAD 重加密）。
/// 
/// 本文件的【派生方法】（BuildFieldAad）定义了字段级动态 AAD 的拼接规则：
///   - 同样属于契约，规则变更需评估影响范围。
/// 
/// 跨系统共享的 AAD 单独放在 <c>CryptoAad.Platform.cs</c>。
/// 单元测试 <c>AadSnapshotTests</c> 会校验所有常量值，任何修改都会让测试失败。
/// </summary>
public static partial class CryptoAad
{
    // ============================================================
    // 授权文件
    // ============================================================

    /// <summary>
    /// 授权文件 payload 内容加密的 AAD。
    /// </summary>
    /// <remarks>
    /// 持久化契约：v1.0 起使用，禁止修改。
    /// 对应 <c>LicenseCryptoService.GenerateLicenseAsync / ValidateLicenseAsync</c>。
    /// </remarks>
    public const string LicensePayload = "license.payload";

    // ============================================================
    // 自检（临时性，不持久化）
    // ============================================================

    /// <summary>
    /// 启动自检的加解密往返探测 AAD。
    /// </summary>
    /// <remarks>
    /// 非持久化：加密后的密文在自检结束即丢弃，不会落库。
    /// 修改此常量不会影响任何历史数据，但仍建议保持稳定以便日志对照。
    /// 对应 <c>StartupSelfCheckService.CheckMasterKeyRoundTripAsync</c>。
    /// </remarks>
    public const string SelfCheckProbe = "selfcheck.probe";

    // ============================================================
    // 动态 AAD 派生
    // ============================================================

    /// <summary>
    /// 派生字段级敏感数据的 AAD（格式：<c>"{group}.{key}"</c>）。
    /// 
    /// 用于 <c>SysSetting</c> 表中 <c>IsEncrypted=1</c> 的字段，例如：
    ///   - FTP 密码：group="storage.targets", key="ftpPassword"
    ///     → AAD = "storage.targets.ftpPassword"
    ///   - SFTP 密码：group="storage.targets", key="sftpPassword"
    ///     → AAD = "storage.targets.sftpPassword"
    /// 
    /// ⚠️ 规则契约：分隔符必须是英文句点 <c>.</c>；
    ///    group 与 key 内不得包含 <c>.</c>，否则会导致歧义。
    /// </summary>
    /// <param name="group">分组键（如 "storage.targets"）。</param>
    /// <param name="key">字段键（如 "ftpPassword"）。</param>
    /// <returns>形如 "group.key" 的 AAD 字符串。</returns>
    public static string BuildFieldAad(string group, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (group.Contains('.') && group.EndsWith('.'))
            throw new ArgumentException("group 不应以句点结尾", nameof(group));

        return $"{group}.{key}";
    }
}
