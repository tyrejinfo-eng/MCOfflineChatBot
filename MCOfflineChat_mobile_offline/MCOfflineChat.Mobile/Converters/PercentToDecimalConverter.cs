using System.Globalization;

namespace MCOfflineChat.Mobile.Converters;

/// <summary>
/// Converts a percentage value (0–100) to a decimal fraction (0.0–1.0)
/// suitable for binding to MAUI's ProgressBar.Progress property.
/// </summary>
public class PercentToDecimalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d) return Math.Clamp(d / 100.0, 0.0, 1.0);
        if (value is float f) return Math.Clamp(f / 100.0, 0.0, 1.0);
        if (value is int i) return Math.Clamp(i / 100.0, 0.0, 1.0);
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
