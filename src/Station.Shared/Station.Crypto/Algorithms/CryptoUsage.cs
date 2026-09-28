namespace Station.Crypto;

/// <summary>
/// 加密用途标识常量。每个用途在 station_crypto_policy 表中对应一条策略记录。
///
/// ⚠️ 持久化契约：
///   - 一旦上线，字符串值禁止修改；
///   - 新增用途时，只需在此添加常量，并在 CryptoDefaults 中提供出厂默认值。
/// </summary>
public static class CryptoUsage
{
    /// <summary>登录密码哈希（用户口令校验）。</summary>
    public const string Password = "password";

    /// <summary>授权文件（只签名，无需加密）。</summary>
    public const string License = "license";

    /// <summary>采集完成的本地文件签名摘要。</summary>
    public const string FileSig = "file_sig";

    /// <summary>数据库中敏感字段加密（如 FTP/SFTP 密码）。</summary>
    public const string SecretField = "secret_field";

    /// <summary>大文件内容加密（本地缓存 + 端到端加密上传）。</summary>
    public const string FileEncryption = "file_encryption";

    /// <summary>平台上报数据签名（报警状态、授权状态等）。</summary>
    public const string Reporting = "reporting";

    /// <summary>记录仪绑定文件的对称 MAC（HMAC-SM3）。</summary>
    public const string RecorderBinding = "recorder_binding";
}
