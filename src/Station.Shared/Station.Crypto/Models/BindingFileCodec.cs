using System.Security.Cryptography;
using System.Text;

namespace Station.Crypto;

/// <summary>
/// 绑定文件（station_bind.ini）序列化 + MAC 计算辅助。
///
/// ⚠️ 持久化契约：
///   1. INI 字段名（禁止修改，否则历史绑定文件失效）
///   2. Canonicalize 拼接规则（禁止修改，否则历史 MAC 无法验证）
///
/// 【INI 格式】
///   [binding]
///   device_serial=...
///   device_model=...
///   user_no=...
///   user_name=...
///   dept_code=...
///   dept_name=...
///   bound_at=2026-09-24T10:30:00.0000000+08:00
///   mac_algo=HMAC-SM3
///   mac=base64(...)
///
/// 【签名串】serial|model|userNo|userName|deptCode|deptName|boundAt(O)
/// </summary>
public static class BindingFileCodec
{
    // ============================================================
    // 常量（持久化契约）
    // ============================================================

    /// <summary>INI 段名。</summary>
    public const string SectionName = "binding";

    /// <summary>绑定文件名。</summary>
    public const string FileName = "station_bind.ini";

    /// <summary>字段：记录仪序列号。</summary>
    public const string FieldSerial = "device_serial";

    /// <summary>字段：记录仪型号。</summary>
    public const string FieldModel = "device_model";

    /// <summary>字段：用户工号。</summary>
    public const string FieldUserNo = "user_no";

    /// <summary>字段：用户名。</summary>
    public const string FieldUserName = "user_name";

    /// <summary>字段：部门编码。</summary>
    public const string FieldDeptCode = "dept_code";

    /// <summary>字段：部门名。</summary>
    public const string FieldDeptName = "dept_name";

    /// <summary>字段：绑定时间（ISO 8601 带时区）。</summary>
    public const string FieldBoundAt = "bound_at";

    /// <summary>字段：MAC 算法标识。</summary>
    public const string FieldMacAlgo = "mac_algo";

    /// <summary>字段：MAC 值（Base64）。</summary>
    public const string FieldMac = "mac";

    // ============================================================
    // 序列化
    // ============================================================

    /// <summary>
    /// 序列化为 INI 文本。
    /// </summary>
    /// <param name="info">绑定信息（Signature 字段被忽略，由 mac 参数决定）。</param>
    /// <param name="macAlgo">MAC 算法标识（CryptoAlgorithm.HmacSm3 等）。</param>
    /// <param name="mac">MAC 值（Base64）。</param>
    public static string Serialize(BindingInfo info, string macAlgo, string mac)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentException.ThrowIfNullOrWhiteSpace(macAlgo);
        ArgumentNullException.ThrowIfNull(mac);

        return string.Join(Environment.NewLine,
            $"[{SectionName}]",
            $"{FieldSerial}={info.DeviceSerial}",
            $"{FieldModel}={info.DeviceModel}",
            $"{FieldUserNo}={info.UserNo}",
            $"{FieldUserName}={info.UserName}",
            $"{FieldDeptCode}={info.DeptCode}",
            $"{FieldDeptName}={info.DeptName}",
            $"{FieldBoundAt}={info.BoundAt:O}",
            $"{FieldMacAlgo}={macAlgo}",
            $"{FieldMac}={mac}");
    }

    // ============================================================
    // 解析
    // ============================================================

    /// <summary>
    /// 解析 INI 文本。必填字段缺失返回 null。
    /// Signature 字段可能为空（旧格式或文件不完整）。
    /// </summary>
    public static BindingInfo? Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var values = ParseIni(content);

        if (!values.TryGetValue(FieldSerial, out var serial)
            || !values.TryGetValue(FieldUserNo, out var userNo)
            || !DateTime.TryParse(values.GetValueOrDefault(FieldBoundAt), out var boundAt))
        {
            return null;
        }

        return new BindingInfo(
            DeviceSerial: serial,
            DeviceModel: values.GetValueOrDefault(FieldModel, string.Empty),
            UserNo: userNo,
            UserName: values.GetValueOrDefault(FieldUserName, string.Empty),
            DeptCode: values.GetValueOrDefault(FieldDeptCode, string.Empty),
            DeptName: values.GetValueOrDefault(FieldDeptName, string.Empty),
            BoundAt: boundAt,
            Signature: values.GetValueOrDefault(FieldMac, string.Empty));
    }

    /// <summary>
    /// 从 INI 文本中提取 MAC 算法标识。用于验证时按文件算法解析 provider。
    /// </summary>
    public static string? GetMacAlgo(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var values = ParseIni(content);
        return values.GetValueOrDefault(FieldMacAlgo);
    }

    // ============================================================
    // Canonicalize + MAC
    // ============================================================

    /// <summary>
    /// 规范化签名串（用于 MAC 计算）。
    /// 格式：serial|model|userNo|userName|deptCode|deptName|boundAt(O)
    ///
    /// ⚠️ 持久化契约：一旦上线禁止修改拼接规则。
    /// </summary>
    public static string Canonicalize(BindingInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        return string.Join('|',
            info.DeviceSerial,
            info.DeviceModel,
            info.UserNo,
            info.UserName,
            info.DeptCode,
            info.DeptName,
            info.BoundAt.ToString("O"));
    }

    /// <summary>
    /// 计算 MAC（Base64）。
    /// </summary>
    public static string ComputeMac(BindingInfo info, byte[] key, IMacProvider mac)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(mac);

        var canonical = Canonicalize(info);
        return mac.Compute(key, Encoding.UTF8.GetBytes(canonical));
    }

    /// <summary>
    /// 验证 MAC（固定时间比较，防时序攻击）。
    /// </summary>
    public static bool VerifyMac(BindingInfo info, byte[] key, IMacProvider mac)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(mac);

        if (string.IsNullOrEmpty(info.Signature)) return false;

        var canonical = Canonicalize(info);
        var expected = mac.Compute(key, Encoding.UTF8.GetBytes(canonical));

        return FixedTimeEquals(expected, info.Signature);
    }

    // ============================================================
    // 内部
    // ============================================================

    private static Dictionary<string, string> ParseIni(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[') || trimmed.StartsWith('#') || !trimmed.Contains('='))
                continue;

            var idx = trimmed.IndexOf('=');
            values[trimmed[..idx].Trim()] = trimmed[(idx + 1)..].Trim();
        }
        return values;
    }

    /// <summary>Base64 解码后固定时间比较。</summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;

        try
        {
            var ba = Convert.FromBase64String(a);
            var bb = Convert.FromBase64String(b);
            return CryptographicOperations.FixedTimeEquals(ba, bb);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
