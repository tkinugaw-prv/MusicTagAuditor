using System.Globalization;
using System.Windows.Data;

namespace MusicTagger.App.Converters;

/// <summary>
/// 重大度の記号を、暗い面の上で見やすい字形へ置き換える。
///
/// ⚠（U+26A0）は輪郭だけの細い三角で描かれるため、塗り潰しの ⛔ / ❓ と並ぶと
/// 一段沈んで見える。同じ意味の塗り潰し字形を持つ Segoe MDL2 Assets へ差し替える。
///
/// **置き換え表に無い記号はそのまま通す。** ビューモデル側の記号が増減しても
/// 表示が欠けないようにするため。
/// </summary>
public sealed class SeverityGlyphConverter : IValueConverter
{
    /// <summary>記号の置き換え表。値は Segoe MDL2 Assets のコードポイント。</summary>
    private static readonly Dictionary<string, string> REPLACEMENTS = new()
    {
        // U+E814 IncidentTriangle。塗り潰しの三角に感嘆符を抜いた字形。
        // 私用領域なのでエスケープで書く。ファイルの文字コード変換で壊さないため。
        ["\u26A0"] = "\uE814",
    };

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string mark)
        {
            return string.Empty;
        }

        return REPLACEMENTS.TryGetValue(mark, out string? replacement) ? replacement : mark;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("逆変換は使わない。");
    }
}
