using System.Globalization;
using System.Windows.Data;
using MusicTagger.Core.Editing;
using MusicTagger.Core.Models;

namespace MusicTagger.App.Converters;

/// <summary>
/// タグのフィールドを日本語の表示名にする。一括入力の選択肢に使う。
/// </summary>
public sealed class TagFieldLabelConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is TagField field ? ManualEditConst.Label(field) : string.Empty;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("逆変換は使わない。");
    }
}
