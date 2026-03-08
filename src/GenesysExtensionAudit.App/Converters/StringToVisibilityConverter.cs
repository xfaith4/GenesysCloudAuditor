using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GenesysExtensionAudit.Converters;

/// <summary>
/// Converts a string to Visibility: Visible when non-empty, Collapsed when null or whitespace.
/// </summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
