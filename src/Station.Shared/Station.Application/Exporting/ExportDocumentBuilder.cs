using System.Text;
using MiniExcelLibs;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Station.Application.Exporting;

/// <summary>
/// 台账/报表导出构建器：CSV（UTF-8 BOM + RFC4180）/ xlsx（MiniExcel）/ PDF（PDFsharp + 系统 CJK 字体）。
/// 三格式共享同一组表头与行数据，权限与数据范围由调用方控制。
/// </summary>
public static class ExportDocumentBuilder
{
    static ExportDocumentBuilder()
    {
        GlobalFontSettings.FontResolver = new SystemCjkFontResolver();
    }

    public static readonly string[] SupportedFormats = ["csv", "xlsx", "pdf"];

    /// <summary>按格式构建导出文件字节流。</summary>
    public static byte[] Build(string format, string title, string[] headers, IEnumerable<string[]> rows)
    {
        return format.ToLowerInvariant() switch
        {
            "xlsx" => BuildXlsx(headers, rows),
            "pdf" => BuildPdf(title, headers, rows),
            _ => BuildCsv(headers, rows)
        };
    }

    /// <summary>CSV：UTF-8 BOM，Excel 可直接打开。</summary>
    public static byte[] BuildCsv(string[] headers, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>xlsx：首行表头 + 数据行，列序按表头顺序。</summary>
    public static byte[] BuildXlsx(string[] headers, IEnumerable<string[]> rows)
    {
        var sheetRows = new List<IDictionary<string, object?>>();
        foreach (var row in rows)
        {
            var dict = new Dictionary<string, object?>();
            for (var i = 0; i < headers.Length && i < row.Length; i++)
            {
                dict[headers[i]] = row[i];
            }

            sheetRows.Add(dict);
        }

        using var ms = new MemoryStream();
        ms.SaveAs(sheetRows, sheetName: "台账");
        return ms.ToArray();
    }

    /// <summary>PDF：A4 横向表格，表头加粗底纹、单元格自动换行、分页与页码。</summary>
    public static byte[] BuildPdf(string title, string[] headers, IEnumerable<string[]> rows)
    {
        var materialized = rows.Select(r => r).ToArray();
        var document = new PdfDocument();
        document.Info.Title = title;

        const double pageWidth = 842;
        const double pageHeight = 595;
        const double margin = 28;
        const double headerTop = 34;
        const double headerHeight = 24;
        const double rowPadding = 5;
        const double footerHeight = 24;

        var titleFont = new XFont(ResolveFontFamily(), 14, XFontStyleEx.Bold);
        var subtitleFont = new XFont(ResolveFontFamily(), 8, XFontStyleEx.Regular);
        var headerFont = new XFont(ResolveFontFamily(), 9, XFontStyleEx.Bold);
        var bodyFont = new XFont(ResolveFontFamily(), 8, XFontStyleEx.Regular);

        var contentWidth = pageWidth - margin * 2;
        var columnWidths = Enumerable.Repeat(contentWidth / headers.Length, headers.Length).ToArray();

        PdfPage page = null!;
        XGraphics gfx = null!;
        double cursorY = 0;
        var pageNumber = 0;
        var totalPages = 1;

        void EnsurePage()
        {
            if (page is not null && cursorY < pageHeight - margin - footerHeight)
            {
                return;
            }

            page = document.AddPage();
            page.Width = XUnit.FromPoint(pageWidth);
            page.Height = XUnit.FromPoint(pageHeight);
            gfx = XGraphics.FromPdfPage(page);
            pageNumber++;

            gfx.DrawString(title, titleFont, XBrushes.Black,
                new XRect(margin, margin - 2, contentWidth, 20), XStringFormats.TopLeft);
            gfx.DrawString(
                $"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}    共 {materialized.Length} 条",
                subtitleFont, XBrushes.Gray,
                new XRect(margin, margin + 20, contentWidth, 13), XStringFormats.TopLeft);

            DrawHeader();
            cursorY = margin + headerTop + headerHeight + 2;
        }

        void DrawHeader()
        {
            var rect = new XRect(margin, margin + headerTop, contentWidth, headerHeight);
            gfx.DrawRectangle(XBrushes.LightGray, rect);
            var x = margin;
            for (var i = 0; i < headers.Length; i++)
            {
                gfx.DrawString(headers[i], headerFont, XBrushes.Black,
                    new XRect(x + 3, rect.Y + 1, columnWidths[i] - 6, rect.Height - 2),
                    XStringFormats.TopLeft);
                x += columnWidths[i];
            }
        }

        void DrawFooter()
        {
            gfx.DrawString($"第 {pageNumber} 页 / 共 {totalPages} 页", subtitleFont, XBrushes.Gray,
                new XRect(margin, pageHeight - margin - 10, contentWidth, 10),
                XStringFormats.BottomRight);
        }

        // 先算总页数（用于页脚）
        {
            var estimatedHeight = margin + headerTop + headerHeight + 2 +
                                  materialized.Sum(r => EstimateRowHeight(r, bodyFont, columnWidths, rowPadding)) +
                                  footerHeight;
            totalPages = Math.Max(1, (int)Math.Ceiling(estimatedHeight / (pageHeight - margin * 2)));
        }

        EnsurePage();
        for (var rowIndex = 0; rowIndex < materialized.Length; rowIndex++)
        {
            var cells = materialized[rowIndex];
            var rowHeight = EstimateRowHeight(cells, bodyFont, columnWidths, rowPadding);
            if (cursorY + rowHeight > pageHeight - margin - footerHeight)
            {
                DrawFooter();
                EnsurePage();
            }

            var x = margin;
            for (var i = 0; i < cells.Length; i++)
            {
                var cellRect = new XRect(x + 3, cursorY, columnWidths[i] - 6, rowHeight - rowPadding);
                DrawWrapped(gfx, cells[i], bodyFont, XBrushes.Black, cellRect);
                x += columnWidths[i];
            }

            // 行分隔线
            gfx.DrawLine(XPens.Gray, margin, cursorY + rowHeight - 1,
                margin + contentWidth, cursorY + rowHeight - 1);
            cursorY += rowHeight;
        }

        DrawFooter();

        using var stream = new MemoryStream();
        document.Save(stream, false);
        return stream.ToArray();
    }

    private static string ResolveFontFamily()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Microsoft YaHei";
        }

        return "Noto Sans CJK SC";
    }

    private static double EstimateRowHeight(string[] cells, XFont font, double[] columnWidths, double padding)
    {
        var lines = 1;
        for (var i = 0; i < cells.Length; i++)
        {
            lines = Math.Max(lines, CountWrappedLines(cells[i], font, columnWidths[i] - 8));
        }

        return lines * (font.Height + 3) + padding;
    }

    private static int CountWrappedLines(string text, XFont font, double width)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 1;
        }

        var lines = 1;
        var current = string.Empty;
        foreach (var ch in text)
        {
            var candidate = current + ch;
            var size = MeasureText(candidate, font);
            if (size.Width > width && current.Length > 0)
            {
                lines++;
                current = ch.ToString();
            }
            else
            {
                current = candidate;
            }
        }

        return lines;
    }

    private static void DrawWrapped(XGraphics gfx, string text, XFont font, XBrush brush, XRect rect)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var y = rect.Y;
        var current = string.Empty;
        foreach (var ch in text)
        {
            var candidate = current + ch;
            if (MeasureText(candidate, font).Width > rect.Width && current.Length > 0)
            {
                gfx.DrawString(current, font, brush, new XRect(rect.X, y, rect.Width, font.Height),
                    XStringFormats.TopLeft);
                y += font.Height + 3;
                current = ch.ToString();
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
        {
            gfx.DrawString(current, font, brush, new XRect(rect.X, y, rect.Width, font.Height),
                XStringFormats.TopLeft);
        }
    }

    private static XSize MeasureText(string text, XFont font) => g_measure.Value!.Measure(text, font);

    private static readonly ThreadLocal<MeasurementContext> g_measure = new(() => new MeasurementContext());

    /// <summary>测量用图形上下文（XGraphics.MeasureString 需要页面上下文，进程内复用一份）。</summary>
    private sealed class MeasurementContext
    {
        private readonly PdfDocument _document;
        private readonly XGraphics _graphics;

        internal MeasurementContext()
        {
            _document = new PdfDocument();
            var page = _document.AddPage();
            _graphics = XGraphics.FromPdfPage(page);
        }

        public XSize Measure(string text, XFont font) => _graphics.MeasureString(text, font);
    }

    private static string EscapeCsv(string? value)
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
