using System.Globalization;
using Avalonia.Data.Converters;

namespace Station.Desktop.Converters;
public sealed class EqualsConverter : IValueConverter
{
    public static readonly EqualsConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => string.Equals(v?.ToString(), p?.ToString(), StringComparison.Ordinal);
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => throw new NotSupportedException();
}
