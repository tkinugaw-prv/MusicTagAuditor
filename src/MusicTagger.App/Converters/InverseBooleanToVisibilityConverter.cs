using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MusicTagger.App.Converters;

/// <summary>
/// 真偽値を反転してから可視性にする。2 択の一方だけを表示する場面で使う。
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("逆変換は使わない。");
    }
}
