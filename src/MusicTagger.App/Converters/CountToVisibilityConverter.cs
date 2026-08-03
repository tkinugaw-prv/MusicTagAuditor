using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MusicTagger.App.Converters;

/// <summary>
/// 件数が 0 なら非表示にする。要確認項目が無いときに空の赤い枠を出さないため。
/// </summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("逆変換は使わない。");
    }
}
