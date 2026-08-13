using PdfSharp.Fonts;

namespace Station.Application.Exporting;

/// <summary>
/// PDF 中文字体解析器：优先使用配置 <c>STATION__EXPORT__FONTFILE</c> 指定的字体文件，
/// 否则探测系统常见 CJK 字体（Windows SimHei/微软雅黑，Linux wqy-microhei/Noto CJK/Droid）。
/// Linux 容器需安装相应字体包（如 fonts-wqy-microhei 或 fonts-noto-cjk）。
/// </summary>
public sealed class SystemCjkFontResolver : IFontResolver
{
    public const string FaceName = "CJK";

    private static readonly string[] CandidatePaths = BuildCandidatePaths();
    private static readonly Lazy<string> FontPath = new(FindFontPath);

    private static string[] BuildCandidatePaths()
    {
        var envOverride = Environment.GetEnvironmentVariable("STATION__EXPORT__FONTFILE");
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            candidates.Add(envOverride);
        }

        if (OperatingSystem.IsWindows())
        {
            var fonts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
            candidates.AddRange(
            [
                Path.Combine(fonts, "simhei.ttf"),
                Path.Combine(fonts, "msyh.ttc"),
                Path.Combine(fonts, "simsun.ttc"),
                Path.Combine(fonts, "Deng.ttf"),
                Path.Combine(fonts, "simkai.ttf"),
                Path.Combine(fonts, "simfang.ttf")
            ]);
        }
        else
        {
            candidates.AddRange(
            [
                "/usr/share/fonts/truetype/wqy/wqy-microhei.ttc",
                "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc",
                "/usr/share/fonts/opentype/noto/NotoSansCJKsc-Regular.otf",
                "/usr/share/fonts/truetype/droid/DroidSansFallbackFull.ttf",
                "/usr/share/fonts/truetype/arphic/uming.ttc"
            ]);
        }

        return candidates.ToArray();
    }

    private static string FindFontPath()
    {
        foreach (var path in CandidatePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new InvalidOperationException(
            "未找到可用的中文字体文件，无法生成中文 PDF。请设置环境变量 STATION__EXPORT__FONTFILE 指向一个 TTF/OTF/TTC 字体，" +
            "或安装系统 CJK 字体（Windows：SimHei/微软雅黑；Linux：fonts-wqy-microhei 或 fonts-noto-cjk）。");
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (familyName is FaceName or "Microsoft YaHei" or "Noto Sans CJK SC" or "SimHei" or "WenQuanYi Micro Hei")
        {
            // TTC 集合取首个常规字重；Windows 默认命中 SimHei（TTF），无需集合索引
            return new FontResolverInfo(FaceName, bold, italic);
        }

        return null;
    }

    public byte[]? GetFont(string faceName) =>
        faceName == FaceName ? File.ReadAllBytes(FontPath.Value) : null;
}
