using System.Globalization;
using System.Windows.Data;

namespace MusicTagger.App.Converters;

/// <summary>
/// 列挙値がパラメータと一致するかを真偽値にする。ラジオボタンで列挙を選ばせるために使う。
/// </summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not null
            && parameter is string name
            && string.Equals(value.ToString(), name, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // チェックが外れた側からも通知が来る。選ばれた側だけを反映する。
        if (value is not true || parameter is not string name)
        {
            return Binding.DoNothing;
        }

        return Enum.Parse(Nullable.GetUnderlyingType(targetType) ?? targetType, name);
    }
}
