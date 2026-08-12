using System.Text;

namespace AlbumProbe;

/// <summary>
/// 測定結果を Markdown で書き出す。画面とファイルへ同時に出す。
///
/// **表を組み立てる側が Markdown の記法を知らずに済むようにする。**
/// 測定ごとに区切りやエスケープを書き分けると、レポートの体裁が測定ごとにずれる。
/// </summary>
public sealed class ReportWriter
{
    /// <summary>ファイルへ書き出すための蓄積。</summary>
    private readonly StringBuilder _buffer = new();

    /// <summary>
    /// 1 行書く。
    /// </summary>
    /// <param name="line">本文。省略すると空行。</param>
    public void Line(string line = "")
    {
        Console.WriteLine(line);
        _buffer.AppendLine(line);
    }

    /// <summary>
    /// 見出しを書く。前に空行を入れる。
    /// </summary>
    /// <param name="level">見出しの深さ。</param>
    /// <param name="text">見出し。</param>
    public void Heading(int level, string text)
    {
        Line();
        Line($"{new string('#', level)} {text}");
        Line();
    }

    /// <summary>
    /// 表の見出し行と区切り行を書く。
    /// </summary>
    /// <param name="columns">列名。</param>
    public void TableHeader(params string[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        Line($"| {string.Join(" | ", columns)} |");
        Line($"|{string.Concat(Enumerable.Repeat("---|", columns.Length))}");
    }

    /// <summary>
    /// 表の行を書く。
    /// </summary>
    /// <param name="cells">セル。</param>
    public void TableRow(params string[] cells)
    {
        ArgumentNullException.ThrowIfNull(cells);

        // セル内の | は表を壊すので落とす。タグの値に現れうる。
        Line($"| {string.Join(" | ", cells.Select(cell => cell.Replace("|", "/", StringComparison.Ordinal)))} |");
    }

    /// <summary>
    /// 蓄積した内容をファイルへ書き出す。
    /// </summary>
    /// <param name="path">出力先。</param>
    /// <param name="cancellationToken">キャンセル用トークン。</param>
    /// <returns>書き出しの完了。</returns>
    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        await File.WriteAllTextAsync(path, _buffer.ToString(), new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
    }
}
