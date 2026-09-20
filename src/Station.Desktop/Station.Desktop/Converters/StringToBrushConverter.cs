// 放置位置：Station.Desktop/Converters/StringToBrushConverter.cs
// 说明：AuditLogDto.ResultColor 是字符串（Application 层不引用 Avalonia），
//       由本转换器在 UI 层转成画刷并缓存。

using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Station.Desktop.Converters;

/// <summary>"#RRGGBB" / "#AARRGGBB" 颜色字符串转画刷（带缓存，只读共享）。</summary>
public sealed class StringToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, IBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        lock (Cache)
        {
            if (Cache.TryGetValue(text, out var cached))
            {
                return cached;
            }

            IBrush brush;
            try
            {
                brush = new SolidColorBrush(Color.Parse(text));
            }
            catch (Exception)
            {
                return null;
            }

            Cache[text] = brush;
            return brush;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
