using System.Globalization;
using System.Windows.Data;

namespace MusicTagAuditor.App.Converters;

/// <summary>
/// 真偽値を反転する。2 択のラジオボタンで、片方だけを持つプロパティに双方向で結ぶために使う。
/// </summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not true;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not true;
    }
}
