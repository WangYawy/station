using System.Text;
using Station.Crypto.Engine.Models;

namespace Station.Crypto.Engine.Binding;

/// <summary>
/// 记录仪绑定文件引擎（纯编排，无 DI，无日志，无文件 IO）。
///
/// 【职责】绑定信息 + MAC 密钥 → station_bind.ini 内容；反之验证。
/// 【不负责】主密钥读取、HKDF 派生、算法解析 —— 这些由调用方处理。
///
/// 【调用方】
///   - Tools.BindingFileTool：传 AlgorithmRegistry.Default.GetMacProvider
///   - Infrastructure（未来）：传 ICryptoPolicyService 解析出的 macProvider
/// </summary>
public static class BindingEngine
{
    /// <summary>INI 段名（持久化契约，禁止修改）。</summary>
    public const string SectionName = "binding";

    /// <summary>
    /// 生成绑定文件内容。
    /// </summary>
    /// <param name="info">绑定信息（Mac 字段被忽略）。</param>
    /// <param name="bindingKey">已派生的 Binding MAC 密钥（32 字节）。</param>
    /// <param name="macProvider">已解析的 MAC 实现。</param>
    public static string Build(
        BindingInfo info,
        byte[] bindingKey,
        IMacProvider macProvider)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(bindingKey);
        ArgumentNullException.ThrowIfNull(macProvider);

        var mac = ComputeMac(info, bindingKey, macProvider);

        return string.Join(Environment.NewLine,
            $"[{SectionName}]",
            $"device_serial={info.DeviceSerial}",
            $"device_model={info.DeviceModel}",
            $"user_no={info.UserNo}",
            $"user_name={info.UserName}",
            $"dept_code={info.DeptCode}",
            $"dept_name={info.DeptName}",
            $"bound_at={info.BoundAt:O}",
            $"mac_algo={macProvider.Algorithm}",
            $"mac={mac}");
    }

    /// <summary>
    /// 验证绑定文件内容。
    /// </summary>
    /// <param name="content">ini 文本。</param>
    /// <param name="bindingKey">已派生的 Binding MAC 密钥。</param>
    /// <param name="macResolver">按算法标识解析 MAC 的委托。</param>
    /// <returns>验证通过返回 true；任何格式/算法/校验失败返回 false。</returns>
    public static bool Validate(
        string content,
        byte[] bindingKey,
        Func<string, IMacProvider> macResolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentNullException.ThrowIfNull(bindingKey);
        ArgumentNullException.ThrowIfNull(macResolver);

        var values = ParseIni(content);

        if (!values.TryGetValue("recorder_serial", out var serial)
            || !values.TryGetValue("user_no", out var userNo)
            || !DateTime.TryParse(values.GetValueOrDefault("bound_at"), out var boundAt))
        {
            return false;
        }

        var macAlgo = values.GetValueOrDefault("mac_algo") ?? CryptoAlgorithm.HmacSm3;
        var expectedMac = values.GetValueOrDefault("mac") ?? string.Empty;

        var info = new BindingInfo(
            serial,
            values.GetValueOrDefault("recorder_model", string.Empty),
            userNo,
            values.GetValueOrDefault("user_name", string.Empty),
            values.GetValueOrDefault("dept_code", string.Empty),
            values.GetValueOrDefault("dept_name", string.Empty),
            boundAt,
            expectedMac);

        IMacProvider mac;
        try { mac = macResolver(macAlgo); }
        catch (NotSupportedException) { return false; }

        var actualMac = ComputeMac(info, bindingKey, mac);
        return string.Equals(actualMac, expectedMac, StringComparison.Ordinal);
    }

    /// <summary>
    /// 查看绑定信息（不做验证）。
    /// </summary>
    /// <returns>解析成功返回 info；失败返回 null。</returns>
    public static BindingInfo? Inspect(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        try
        {
            var values = ParseIni(content);

            if (!values.TryGetValue("recorder_serial", out var serial)
                || !values.TryGetValue("user_no", out var userNo)
                || !DateTime.TryParse(values.GetValueOrDefault("bound_at"), out var boundAt))
            {
                return null;
            }

            return new BindingInfo(
                serial,
                values.GetValueOrDefault("recorder_model", string.Empty),
                userNo,
                values.GetValueOrDefault("user_name", string.Empty),
                values.GetValueOrDefault("dept_code", string.Empty),
                values.GetValueOrDefault("dept_name", string.Empty),
                boundAt,
                values.GetValueOrDefault("mac", string.Empty));
        }
        catch
        {
            return null;
        }
    }

    // ================================================================
    // 内部
    // ================================================================

    private static string ComputeMac(
        BindingInfo info, byte[] key, IMacProvider mac)
    {
        var canonical = string.Join('|',
            info.DeviceSerial, info.DeviceModel, info.UserNo, info.UserName,
            info.DeptCode, info.DeptName, info.BoundAt.ToString("O"));

        return mac.Compute(key, Encoding.UTF8.GetBytes(canonical));
    }

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
}
