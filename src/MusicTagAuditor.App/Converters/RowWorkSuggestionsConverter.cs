using System.Globalization;
using System.Windows.Data;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.App.Converters;

/// <summary>
/// 行の作曲家から、タイトルのセルに出す作品名の候補を引く。
///
/// 値は順に「行」「作品候補の索引」。行に辞書を持たせずに済むよう、View の側で組み合わせる。
/// </summary>
public sealed class RowWorkSuggestionsConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);

        return values is [TrackRowViewModel row, WorkSuggestionIndex index, ..]
            ? index.For([row.GetValuesForDictionary(TagField.Composer)])
            : Array.Empty<SuggestionEntry>();
    }

    /// <inheritdoc />
    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("逆変換は使わない。");
    }
}
