using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FileUnblocker;

/// <summary>
/// Returns Visibility.Collapsed when the bound string is null or empty,
/// Visibility.Visible otherwise — used to hide trend labels when there is no delta.
/// </summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class NullOrEmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
   string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    throw new NotSupportedException();
}
