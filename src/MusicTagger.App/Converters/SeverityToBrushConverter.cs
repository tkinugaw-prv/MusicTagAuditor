using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MusicTagger.Core.Models;

namespace MusicTagger.App.Converters;

/// <summary>
/// 重大度を記号の表示色にする。
///
/// 記号（⛔ / ⚠ / ❓ / ✎）はどれも同じ濃さの字形で出るため、暗い面の上では
/// 形の違いだけで重大度を読み分けることになる。色を足して一覧のまま区別できるようにする。
///
/// 色はテーマのリソースから引く。ここに値を書くと Themes/DarkTheme.xaml と二重管理になる。
/// </summary>
public sealed class SeverityToBrushConverter : IValueConverter
{
    /// <summary>重大度ごとのブラシのリソースキー。</summary>
    private static readonly Dictionary<Severity, string> BRUSH_KEYS = new()
    {
        [Severity.Error] = "DangerBrush",
        [Severity.Warning] = "WarningBrush",
        [Severity.Info] = "InfoBrush",
        [Severity.Manual] = "AccentBrush",
    };

    /// <summary>リソースを引けなかったときの色。</summary>
    private const string FALLBACK_BRUSH_KEY = "TextPrimaryBrush";

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Severity severity || !BRUSH_KEYS.TryGetValue(severity, out string? key))
        {
            key = FALLBACK_BRUSH_KEY;
        }

        return FindBrush(key) ?? FindBrush(FALLBACK_BRUSH_KEY) ?? Brushes.White;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("逆変換は使わない。");
    }

    /// <summary>
    /// テーマリソースからブラシを引く。
    /// </summary>
    /// <param name="resourceKey">リソースキー。</param>
    /// <returns>見つかったブラシ。無ければ null。</returns>
    private static Brush? FindBrush(string resourceKey)
    {
        return Application.Current?.TryFindResource(resourceKey) as Brush;
    }
}
