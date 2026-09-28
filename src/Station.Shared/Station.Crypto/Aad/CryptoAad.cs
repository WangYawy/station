namespace Station.Crypto;

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
/// </summary>
public static class CryptoAad
{
    /// <summary>
    /// 授权文件 payload 内容加密的 AAD（预留）。
    /// 当前授权采用"只签名不加密"模式，此常量保留给未来加密模式使用。
    /// </summary>
    public const string LicensePayload = "license.payload";

    /// <summary>
    /// 启动自检的加解密往返探测 AAD。
    /// 非持久化：加密后的密文在自检结束即丢弃，不会落库。
    /// </summary>
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

        if (group.EndsWith('.'))
            throw new ArgumentException("group 不应以句点结尾", nameof(group));
        if (key.Contains('.'))
            throw new ArgumentException("key 不应包含句点", nameof(key));

        return $"{group}.{key}";
    }
}
