using System.Globalization;
using System.Windows.Data;

namespace MusicTagAuditor.App.Converters;

/// <summary>
/// フォルダの相対パスを表示用の文字列にする。ファイル一覧のフォルダ列に使う。
///
/// ライブラリルート直下のファイルは相対パスにフォルダ部分が無く、空文字になる。
/// **空欄のまま出すと「ルート直下」なのか「値が取れなかった」のか画面から区別できない。**
/// そこで表示のときだけ目印を入れる。絞り込みや並べ替えが使う値は変換しない
/// （<c>FolderPath</c> はフォルダツリーの絞り込みで文字列比較の土台になっている）。
/// </summary>
public sealed class RootFolderLabelConverter : IValueConverter
{
    /// <summary>ルート直下であることを示す表示文字列。</summary>
    private const string ROOT_LABEL = "(root)";

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string folderPath && folderPath.Length > 0 ? folderPath : ROOT_LABEL;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("逆変換は使わない。");
    }
}
