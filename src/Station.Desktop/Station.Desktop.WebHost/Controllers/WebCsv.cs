using System.Text;

namespace Station.Desktop.WebHost.Controllers;

/// <summary>CSV 构建：UTF-8 BOM + RFC4180 转义（单机 Web 导出）。</summary>
public static class WebCsv
{
    public static byte[] Build(string[] headers, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine(string.Join(",", headers.Select(Escape)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", row.Select(Escape)));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static string MaskIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return string.Empty;
        }

        var parts = ip.Split('.');
        return parts.Length == 4 ? $"{parts[0]}.{parts[1]}.{parts[2]}.*" : ip;
    }

    private static string Escape(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
