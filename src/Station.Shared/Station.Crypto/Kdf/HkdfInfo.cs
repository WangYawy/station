namespace Station.Crypto;

/// <summary>
/// HKDF info 字符串常量。
///
/// ⚠️ 持久化契约：
///   - 一旦某用途用于生产数据，对应的 info 值禁止修改；
///   - 如需变更，必须走数据迁移流程（旧密钥解密 → 用新 info 派生密钥重加密）；
///   - 命名约定：station:{用途}:v{版本}。
/// </summary>
public static class HkdfInfo
{
    /// <summary>文件内容加密密钥派生。用于 STFE 文件分块加密。</summary>
    public const string FileEncryption = "station:file-encryption:v1";

    /// <summary>记录仪绑定文件 MAC 密钥派生。</summary>
    public const string RecorderBinding = "station:recorder-binding:v1";

    /// <summary>授权文件签名密钥派生（预留，当前只签名用 PEM 私钥，不派生）。</summary>
    public const string LicenseSigning = "station:license-signing:v1";

    /// <summary>平台上报签名密钥派生（预留）。</summary>
    public const string ReportingSigning = "station:reporting-signing:v1";
}
