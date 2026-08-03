using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MusicTagger.App.Converters;

/// <summary>
/// 列挙値がパラメータと一致するときだけ表示する。種別によって入力欄を出し分けるために使う。
/// </summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool matches = value is not null
            && parameter is string name
            && string.Equals(value.ToString(), name, StringComparison.Ordinal);

        return matches ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("逆変換は使わない。");
    }
}
