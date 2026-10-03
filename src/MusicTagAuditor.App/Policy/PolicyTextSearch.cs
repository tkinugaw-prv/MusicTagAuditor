using System.Text;
using System.Windows.Documents;

namespace MusicTagAuditor.App.Policy;

/// <summary>
/// 原則文書の中を文字列で探す。
///
/// **段落単位で文字列をつないでから探す。** 太字やコードの境目で <see cref="Run"/> が分かれるため、
/// Run ごとに探すと「`composer` を先に確定」のような装飾をまたぐ語が見つからない。
/// </summary>
public static class PolicyTextSearch
{
    /// <summary>
    /// 一致した範囲をすべて返す。大文字・小文字は区別しない。
    /// </summary>
    /// <param name="blocks">探す範囲のブロック要素。</param>
    /// <param name="query">探す文字列。</param>
    /// <returns>文書の出現順の一致範囲。</returns>
    public static IReadOnlyList<TextRange> FindAll(IEnumerable<Block> blocks, string query)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        List<TextRange> results = [];

        if (string.IsNullOrWhiteSpace(query))
        {
            return results;
        }

        foreach (Paragraph paragraph in EnumerateParagraphs(blocks))
        {
            FindInParagraph(paragraph, query.Trim(), results);
        }

        return results;
    }

    /// <summary>
    /// 一致する件数を数える。
    /// </summary>
    /// <param name="blocks">探す範囲のブロック要素。</param>
    /// <param name="query">探す文字列。</param>
    /// <returns>件数。</returns>
    public static int Count(IEnumerable<Block> blocks, string query)
    {
        return FindAll(blocks, query).Count;
    }

    /// <summary>
    /// ブロック要素の配下にある段落を文書順に列挙する。
    /// </summary>
    private static IEnumerable<Paragraph> EnumerateParagraphs(IEnumerable<Block> blocks)
    {
        foreach (Block block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    yield return paragraph;
                    break;
                case Section section:
                    foreach (Paragraph child in EnumerateParagraphs(section.Blocks))
                    {
                        yield return child;
                    }

                    break;
                case List list:
                    foreach (ListItem item in list.ListItems)
                    {
                        foreach (Paragraph child in EnumerateParagraphs(item.Blocks))
                        {
                            yield return child;
                        }
                    }

                    break;
                case Table table:
                    foreach (TableRowGroup group in table.RowGroups)
                    {
                        foreach (TableRow row in group.Rows)
                        {
                            foreach (TableCell cell in row.Cells)
                            {
                                foreach (Paragraph child in EnumerateParagraphs(cell.Blocks))
                                {
                                    yield return child;
                                }
                            }
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// 段落 1 つの中を探して、一致範囲を書き足す。
    /// </summary>
    private static void FindInParagraph(Paragraph paragraph, string query, List<TextRange> results)
    {
        StringBuilder text = new();
        List<(Run Run, int Start)> runs = [];
        CollectRuns(paragraph.Inlines, text, runs);

        string content = text.ToString();
        int index = content.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);

        while (index >= 0)
        {
            TextPointer? start = ToPointer(runs, index, isEnd: false);
            TextPointer? end = ToPointer(runs, index + query.Length, isEnd: true);

            if (start is not null && end is not null)
            {
                results.Add(new TextRange(start, end));
            }

            index = content.IndexOf(query, index + query.Length, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    /// <summary>
    /// 行内要素から Run を集め、段落内での開始位置を記録する。
    /// </summary>
    private static void CollectRuns(InlineCollection inlines, StringBuilder text, List<(Run Run, int Start)> runs)
    {
        foreach (Inline inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    runs.Add((run, text.Length));
                    text.Append(run.Text);
                    break;
                case Span span:
                    CollectRuns(span.Inlines, text, runs);
                    break;
                case LineBreak:
                    // 改行をまたいで一致させない。区切りとして 1 文字入れる。
                    text.Append('\n');
                    break;
            }
        }
    }

    /// <summary>
    /// 段落内の位置を文書上の位置に直す。
    /// </summary>
    /// <param name="runs">段落内の Run と開始位置。</param>
    /// <param name="offset">段落内の位置。</param>
    /// <param name="isEnd">範囲の終端か。終端は Run の末尾に一致してよい。</param>
    private static TextPointer? ToPointer(List<(Run Run, int Start)> runs, int offset, bool isEnd)
    {
        foreach ((Run run, int start) in runs)
        {
            int end = start + run.Text.Length;
            bool inside = isEnd
                ? offset > start && offset <= end
                : offset >= start && offset < end;

            if (inside)
            {
                return run.ContentStart.GetPositionAtOffset(offset - start);
            }
        }

        return null;
    }
}
