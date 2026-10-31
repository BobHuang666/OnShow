using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OnShow.App;

/// <summary>BarRatio &lt; 0 → Collapsed(无额度上限时不显示进度条)。</summary>
public sealed class BarRatioToVisibilityConverter : IValueConverter
{
    public static readonly BarRatioToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d && d >= 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → Visibility(标准非反转)。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}
