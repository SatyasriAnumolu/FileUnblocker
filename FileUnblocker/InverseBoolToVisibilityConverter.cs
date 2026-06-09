using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FileUnblocker;

/// <summary>
/// Converts bool ? Visibility in inverse: True ? Collapsed, False ? Visible.
/// Used to show the empty-state overlay when the results list has no items.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}
