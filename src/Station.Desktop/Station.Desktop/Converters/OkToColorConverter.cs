using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Station.Desktop.Converters;

public sealed class OkToColorConverter : IValueConverter
{
    public static readonly OkToColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Brushes.SeaGreen : Brushes.OrangeRed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class OkToBackgroundConverter : IValueConverter
{
    public static readonly OkToBackgroundConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? new SolidColorBrush(Color.Parse("#f0fdf4"))
                      : new SolidColorBrush(Color.Parse("#fef2f2"));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
