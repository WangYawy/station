using System.Text;
using Station.Crypto.KeyGen;
using Station.Crypto.Kdf;
using Station.Crypto.Providers.Macs;
using Station.Tools.Core.Models;

namespace Station.Tools.Core.Binding;

/// <summary>记录仪绑定文件工具。</summary>
public static class BindingFileTool
{
    private const string HkdfInfo = "station:recorder-binding:v1";

    /// <summary>生成绑定文件内容。</summary>
    public static ToolResult<string> Generate(
        BindingInfo info,
        MasterKeyFile masterKeyFile,
        string algorithm = "HMAC-SM3")
    {
        try
        {
            var mac = ComputeMac(info, masterKeyFile, algorithm);
            var content = string.Join(Environment.NewLine,
                "[binding]",
                $"recorder_serial={info.RecorderSerial}",
                $"recorder_model={info.RecorderModel}",
                $"user_no={info.UserNo}",
                $"user_name={info.UserName}",
                $"dept_code={info.DeptCode}",
                $"dept_name={info.DeptName}",
                $"bound_at={info.BoundAt:O}",
                $"mac_algo={algorithm}",
                $"mac={mac}");

            return ToolResult<string>.Ok(content);
        }
        catch (Exception ex)
        {
            return ToolResult<string>.Fail("BINDING_GEN_FAIL", ex.Message);
        }
    }

    /// <summary>验证绑定文件内容。</summary>
    public static ToolResult<bool> Validate(string content, MasterKeyFile masterKeyFile)
    {
        try
        {
            var values = ParseIni(content);

            if (!values.TryGetValue("recorder_serial", out var serial) ||
                !values.TryGetValue("user_no", out var userNo) ||
                !DateTime.TryParse(values.GetValueOrDefault("bound_at"), out var boundAt))
                return ToolResult<bool>.Fail("PARSE_FAIL", "绑定文件格式错误");

            var macAlgo = values.GetValueOrDefault("mac_algo") ?? "HMAC-SM3";
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

            var actualMac = ComputeMac(info, masterKeyFile, macAlgo);
            return ToolResult<bool>.Ok(string.Equals(actualMac, expectedMac, StringComparison.Ordinal));
        }
        catch (Exception ex)
        {
            return ToolResult<bool>.Fail("BINDING_VERIFY_FAIL", ex.Message);
        }
    }

    /// <summary>查看绑定信息。</summary>
    public static ToolResult<BindingInfo> Inspect(string content)
    {
        try
        {
            var values = ParseIni(content);

            if (!values.TryGetValue("recorder_serial", out var serial) ||
                !values.TryGetValue("user_no", out var userNo) ||
                !DateTime.TryParse(values.GetValueOrDefault("bound_at"), out var boundAt))
                return ToolResult<BindingInfo>.Fail("PARSE_FAIL", "绑定文件格式错误");

            var info = new BindingInfo(
                serial,
                values.GetValueOrDefault("recorder_model", string.Empty),
                userNo,
                values.GetValueOrDefault("user_name", string.Empty),
                values.GetValueOrDefault("dept_code", string.Empty),
                values.GetValueOrDefault("dept_name", string.Empty),
                boundAt,
                values.GetValueOrDefault("mac", string.Empty));

            return ToolResult<BindingInfo>.Ok(info);
        }
        catch (Exception ex)
        {
            return ToolResult<BindingInfo>.Fail("INSPECT_FAIL", ex.Message);
        }
    }

    private static string ComputeMac(BindingInfo info, MasterKeyFile keyFile, string algorithm)
    {
        // 从主密钥 HKDF 派生 Binding 密钥
        var master = keyFile.GetKey(keyFile.Current);
        var bindingKey = HkdfSm3.Derive(master, HkdfInfo, 32);

        var canonical = string.Join('|',
            info.RecorderSerial, info.RecorderModel, info.UserNo, info.UserName,
            info.DeptCode, info.DeptName, info.BoundAt.ToString("O"));

        var macProvider = algorithm.ToUpperInvariant() switch
        {
            "HMAC-SM3" => (Station.Crypto.Abstractions.IMacProvider)new HmacSm3Provider(),
            "HMAC-SHA256" => new HmacSha256Provider(),
            _ => throw new NotSupportedException($"不支持的 MAC 算法：{algorithm}")
        };

        return macProvider.Compute(bindingKey, Encoding.UTF8.GetBytes(canonical));
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
