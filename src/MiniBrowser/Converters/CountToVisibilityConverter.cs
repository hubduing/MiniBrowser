using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MiniBrowser.Converters;

/// <summary>
/// Ненулевое число — Visible, ноль — Collapsed. Обратного пути нет: видимость нигде не правит счётчиком.
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int count && count != 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
