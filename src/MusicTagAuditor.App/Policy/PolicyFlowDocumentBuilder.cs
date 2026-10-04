using System.Globalization;
using System.Net;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;
using WpfList = System.Windows.Documents.List;
using WpfTable = System.Windows.Documents.Table;
using WpfTableCell = System.Windows.Documents.TableCell;
using WpfTableRow = System.Windows.Documents.TableRow;

namespace MusicTagAuditor.App.Policy;

/// <summary>
/// 原則文書の構文木から WPF の <see cref="FlowDocument"/> を組み立てる。
///
/// WebView2 で HTML として出す案は採らなかった。ランタイムへの依存が増え、
/// ダークテーマの配色を CSS で二重に持つことになる。原則が使う構文は
/// 見出し・段落・リスト・表・コードに限られるので、直接組み立てても量は小さい。
/// </summary>
public sealed class PolicyFlowDocumentBuilder
{
    /// <summary>見出しの深さごとの文字サイズ。添字は <c>#</c> の数。</summary>
    private static readonly double[] HEADING_FONT_SIZES = [0, 22, 18, 15.5, 14, 13.5, 13];

    /// <summary>
    /// 表の列幅の比率の下限（半角換算の文字数）。極端に狭い列を避ける。
    /// 「件数」のような 2 文字の見出しが折り返さない幅にする。
    /// </summary>
    private const int TABLE_COLUMN_WEIGHT_MIN = 6;

    /// <summary>表の列幅の比率の上限（半角換算の文字数）。</summary>
    private const int TABLE_COLUMN_WEIGHT_MAX = 60;

    /// <summary>画面のテーマ。単体テストでは null になり、既定色で組み立てる。</summary>
    private readonly ResourceDictionary? _resources;

    /// <summary>
    /// 組み立て係を作る。
    /// </summary>
    /// <param name="resources">色・書体を引くリソース。null なら既定色を使う。</param>
    public PolicyFlowDocumentBuilder(ResourceDictionary? resources)
    {
        _resources = resources;
    }

    /// <summary>
    /// 原則文書を組み立てる。
    /// </summary>
    /// <param name="document">原則文書。</param>
    /// <returns>組み立て結果。</returns>
    public PolicyRendering Build(PolicyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        FlowDocument flow = new()
        {
            PagePadding = new Thickness(28, 20, 28, 40),
            FontFamily = GetResource("AppFontFamily", new FontFamily("Yu Gothic UI")),
            FontSize = 13.5,
            LineHeight = 22,
            Foreground = GetResource<Brush>("TextPrimaryBrush", Brushes.Black),
            Background = GetResource<Brush>("BgOuterBrush", Brushes.White),
            TextAlignment = TextAlignment.Left,
            IsHyphenationEnabled = false,
        };

        Dictionary<HeadingBlock, PolicyHeading> headingByBlock = document.Headings.ToDictionary(heading => heading.Block);
        Dictionary<PolicyHeading, Paragraph> headingParagraphs = [];
        List<PolicyChapterFold> folds = [];
        Dictionary<PolicyHeading, List<WpfList>> orderedLists = [];
        PolicyHeading? currentHeading = null;

        // 折りたたむ章の本文は Section にまとめ、文書には入れずに持っておく。
        // FlowDocument の要素には Visibility が無いため、出し入れで開閉する。
        PolicyChapterFold? currentFold = null;

        foreach (MdBlock block in document.Syntax)
        {
            if (block is HeadingBlock headingBlock && headingByBlock.TryGetValue(headingBlock, out PolicyHeading? heading))
            {
                Paragraph paragraph = CreateHeading(headingBlock);
                headingParagraphs[heading] = paragraph;
                currentHeading = heading;

                if (heading.Level <= 2)
                {
                    currentFold = null;

                    flow.Blocks.Add(paragraph);

                    if (heading.Level == 2 && heading.IsInCollapsedChapter)
                    {
                        int sectionCount = document.Headings.Count(candidate =>
                            candidate.Level == 3 && candidate.ChapterNumber == heading.ChapterNumber);
                        currentFold = CreateFold(heading, paragraph, sectionCount);
                        flow.Blocks.Add(currentFold.TogglePanel);
                        folds.Add(currentFold);
                    }

                    continue;
                }

                AddBlock(flow, currentFold, paragraph);
                continue;
            }

            foreach (WpfBlock rendered in RenderBlock(block))
            {
                AddBlock(flow, currentFold, rendered);

                // 「3.5 規則5」の規則は節の直下の番号付きリストの項目にあたる。参照から項目へ飛ぶために控えておく。
                if (currentHeading is not null && rendered is WpfList { MarkerStyle: TextMarkerStyle.Decimal } list)
                {
                    if (!orderedLists.TryGetValue(currentHeading, out List<WpfList>? lists))
                    {
                        lists = [];
                        orderedLists[currentHeading] = lists;
                    }

                    lists.Add(list);
                }
            }
        }

        return new PolicyRendering(
            flow,
            headingParagraphs,
            folds,
            orderedLists.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<WpfList>)pair.Value));
    }

    /// <summary>
    /// 本文の要素を文書か、折りたたみ中の章の本体に足す。
    /// </summary>
    private static void AddBlock(FlowDocument flow, PolicyChapterFold? fold, WpfBlock block)
    {
        if (fold is null)
        {
            flow.Blocks.Add(block);
        }
        else
        {
            fold.Body.Blocks.Add(block);
        }
    }

    /// <summary>
    /// 折りたたむ章の開閉部品を作る。
    /// </summary>
    private PolicyChapterFold CreateFold(PolicyHeading heading, Paragraph headingParagraph, int sectionCount)
    {
        Paragraph toggle = new()
        {
            Margin = new Thickness(0, 2, 0, 12),
            Padding = new Thickness(12, 8, 12, 8),
            Background = GetResource<Brush>("NoticePanelBrush", Brushes.LightYellow),
            BorderBrush = GetResource<Brush>("NoticePanelBorderBrush", Brushes.Goldenrod),
            BorderThickness = new Thickness(1),
            FontSize = 12.5,
        };

        Hyperlink link = new()
        {
            Foreground = GetResource<Brush>("AccentBrush", Brushes.RoyalBlue),
        };

        Run description = new();
        toggle.Inlines.Add(description);
        toggle.Inlines.Add(link);

        Section body = new();

        return new PolicyChapterFold(heading, headingParagraph, toggle, description, link, body, sectionCount);
    }

    /// <summary>
    /// 見出しを作る。
    /// </summary>
    private Paragraph CreateHeading(HeadingBlock block)
    {
        int level = Math.Clamp(block.Level, 1, HEADING_FONT_SIZES.Length - 1);

        Paragraph paragraph = new()
        {
            FontSize = HEADING_FONT_SIZES[level],
            FontWeight = level <= 4 ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(0, level <= 2 ? 26 : 18, 0, 8),
            LineHeight = double.NaN,
        };

        if (level == 2)
        {
            paragraph.BorderBrush = GetResource<Brush>("BorderBrush", Brushes.Gray);
            paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
            paragraph.Padding = new Thickness(0, 0, 0, 6);
        }

        // 「補足」（#####）は判断理由や実測の記録で、規則そのものより一段控えめに出す。
        if (level >= 5)
        {
            paragraph.Foreground = GetResource<Brush>("TextSecondaryBrush", Brushes.DimGray);
        }

        AddInlines(paragraph.Inlines, block.Inline);

        return paragraph;
    }

    /// <summary>
    /// ブロック要素 1 つを組み立てる。
    /// </summary>
    private IEnumerable<WpfBlock> RenderBlock(MdBlock block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                yield return CreateHeading(heading);
                break;
            case ParagraphBlock paragraphBlock:
                Paragraph paragraph = new() { Margin = new Thickness(0, 0, 0, 10) };
                AddInlines(paragraph.Inlines, paragraphBlock.Inline);
                yield return paragraph;
                break;
            case ListBlock list:
                yield return CreateList(list);
                break;
            case MdTable table:
                yield return CreateTable(table);
                break;
            case CodeBlock code:
                yield return CreateCodeBlock(code);
                break;
            case QuoteBlock quote:
                yield return CreateQuote(quote);
                break;
            case ThematicBreakBlock:
                yield return new Paragraph
                {
                    Margin = new Thickness(0, 8, 0, 8),
                    BorderBrush = GetResource<Brush>("BorderBrush", Brushes.Gray),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    FontSize = 1,
                };
                break;
            case LeafBlock leaf:
                // 想定外の葉要素（HTML ブロック等）は原文のまま出す。黙って落とすと原則の一部が画面から消える。
                yield return new Paragraph(new Run(leaf.Lines.ToString())) { Margin = new Thickness(0, 0, 0, 10) };
                break;
            case ContainerBlock container:
                foreach (MdBlock child in container)
                {
                    foreach (WpfBlock rendered in RenderBlock(child))
                    {
                        yield return rendered;
                    }
                }

                break;
        }
    }

    /// <summary>
    /// リストを組み立てる。番号付きリストは原文の開始番号を引き継ぐ（3.5 の規則8 は空行を挟んで始まる）。
    /// </summary>
    private WpfList CreateList(ListBlock block)
    {
        WpfList list = new()
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(22, 0, 0, 0),
            MarkerStyle = block.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
        };

        if (block.IsOrdered
            && int.TryParse(block.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int start))
        {
            list.StartIndex = start;
        }

        foreach (ListItemBlock item in block.OfType<ListItemBlock>())
        {
            ListItem listItem = new();

            foreach (MdBlock child in item)
            {
                foreach (WpfBlock rendered in RenderBlock(child))
                {
                    // 項目内の段落の下余白は詰める。そのままだと項目の間が段落 1 つ分空く。
                    if (rendered is Paragraph paragraph)
                    {
                        paragraph.Margin = new Thickness(0, 0, 0, 3);
                    }
                    else if (rendered is WpfList nested)
                    {
                        nested.Margin = new Thickness(0, 0, 0, 3);
                    }

                    listItem.Blocks.Add(rendered);
                }
            }

            list.ListItems.Add(listItem);
        }

        return list;
    }

    /// <summary>
    /// 表を組み立てる。列幅は列内の最長の表示幅に比例させる（等幅だと「要素」列が無駄に広がる）。
    /// </summary>
    private WpfTable CreateTable(MdTable block)
    {
        List<MdTableRow> rows = [.. block.OfType<MdTableRow>()];
        int columnCount = rows.Count == 0 ? 0 : rows.Max(row => row.Count);

        WpfTable table = new()
        {
            Margin = new Thickness(0, 4, 0, 14),
            CellSpacing = 0,
            BorderBrush = GetResource<Brush>("BorderBrush", Brushes.Gray),
            BorderThickness = new Thickness(1),
        };

        for (int column = 0; column < columnCount; column++)
        {
            int longest = rows
                .Select(row => column < row.Count && row[column] is MdTableCell cell ? GetDisplayWidth(GetCellText(cell)) : 0)
                .DefaultIfEmpty(0)
                .Max();
            int weight = Math.Clamp(longest, TABLE_COLUMN_WEIGHT_MIN, TABLE_COLUMN_WEIGHT_MAX);

            table.Columns.Add(new TableColumn { Width = new GridLength(weight, GridUnitType.Star) });
        }

        TableRowGroup group = new();
        Brush rowBorder = GetResource<Brush>("RowBorderBrush", Brushes.LightGray);
        Brush headerBackground = GetResource<Brush>("PanelBarBrush", Brushes.WhiteSmoke);

        foreach (MdTableRow row in rows)
        {
            WpfTableRow tableRow = new();

            if (row.IsHeader)
            {
                tableRow.Background = headerBackground;
                tableRow.FontWeight = FontWeights.SemiBold;
            }

            foreach (MdTableCell cell in row.OfType<MdTableCell>())
            {
                WpfTableCell tableCell = new()
                {
                    Padding = new Thickness(8, 4, 8, 4),
                    BorderBrush = rowBorder,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                };

                foreach (MdBlock child in cell)
                {
                    foreach (WpfBlock rendered in RenderBlock(child))
                    {
                        rendered.Margin = new Thickness(0);
                        tableCell.Blocks.Add(rendered);
                    }
                }

                tableRow.Cells.Add(tableCell);
            }

            group.Rows.Add(tableRow);
        }

        table.RowGroups.Add(group);

        return table;
    }

    /// <summary>
    /// セルの文字列。列幅の見積もりに使う。
    /// </summary>
    private static string GetCellText(MdTableCell cell)
    {
        return string.Concat(cell.OfType<LeafBlock>().Select(leaf => PolicyDocument.GetPlainText(leaf.Inline)));
    }

    /// <summary>
    /// 表示幅を半角換算で見積もる。和文は 1 文字で半角 2 文字分を占めるため、
    /// 文字数のまま比べると和文の列が狭くなりすぎる。
    /// </summary>
    private static int GetDisplayWidth(string text)
    {
        return text.Sum(character => character >= '\u3000' ? 2 : 1);
    }

    /// <summary>
    /// コードブロックを組み立てる。書式の例示（3.5 のアルバム名など）は等幅で原文どおりに出す。
    /// </summary>
    private Paragraph CreateCodeBlock(CodeBlock block)
    {
        string text = string.Join(Environment.NewLine, block.Lines.Lines
            .Take(block.Lines.Count)
            .Select(line => line.Slice.ToString()));

        return new Paragraph(new Run(text))
        {
            Margin = new Thickness(0, 2, 0, 12),
            Padding = new Thickness(12, 8, 12, 8),
            FontFamily = GetResource("AppMonoFontFamily", new FontFamily("Consolas")),
            Background = GetResource<Brush>("InputBgBrush", Brushes.WhiteSmoke),
            BorderBrush = GetResource<Brush>("BorderBrush", Brushes.Gray),
            BorderThickness = new Thickness(1),
            LineHeight = double.NaN,
        };
    }

    /// <summary>
    /// 引用を組み立てる。
    /// </summary>
    private Section CreateQuote(QuoteBlock block)
    {
        Section section = new()
        {
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(12, 0, 0, 0),
            BorderBrush = GetResource<Brush>("BorderStrongBrush", Brushes.Gray),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Foreground = GetResource<Brush>("TextSecondaryBrush", Brushes.DimGray),
        };

        foreach (MdBlock child in block)
        {
            foreach (WpfBlock rendered in RenderBlock(child))
            {
                section.Blocks.Add(rendered);
            }
        }

        return section;
    }

    /// <summary>
    /// 行内要素を組み立てて足す。
    /// </summary>
    private void AddInlines(InlineCollection target, ContainerInline? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (MdInline inline in source)
        {
            WpfInline? rendered = RenderInline(inline, target);

            if (rendered is not null)
            {
                target.Add(rendered);
            }
        }
    }

    /// <summary>
    /// 行内要素 1 つを組み立てる。
    /// </summary>
    /// <param name="inline">行内要素。</param>
    /// <param name="preceding">直前までに組み立てた要素。ソフト改行の扱いを決めるのに使う。</param>
    private WpfInline? RenderInline(MdInline inline, InlineCollection preceding)
    {
        switch (inline)
        {
            case LiteralInline literal:
                return new Run(literal.Content.ToString());
            case CodeInline code:
                return new Run(code.Content)
                {
                    FontFamily = GetResource("AppMonoFontFamily", new FontFamily("Consolas")),
                    Background = GetResource<Brush>("InputBgBrush", Brushes.WhiteSmoke),
                };
            case LineBreakInline lineBreak:
                if (lineBreak.IsHard)
                {
                    return new LineBreak();
                }

                // 日本語の文中で折り返した行を空白でつなぐと、語の間に不要な空きが入る。
                return EndsWithWideCharacter(preceding) ? null : new Run(" ");
            case HtmlEntityInline entity:
                return new Run(entity.Transcoded.ToString());
            case HtmlInline html:
                return new Run(WebUtility.HtmlDecode(html.Tag));
            case AutolinkInline autolink:
                return new Run(autolink.Url);
            case EmphasisInline emphasis:
                Span span = emphasis.DelimiterCount >= 2
                    ? new Bold()
                    : new Italic();
                AddInlines(span.Inlines, emphasis);
                return span;
            case LinkInline link:
                // 原則は外部へのリンクを持たない。画面から既定ブラウザを開かせる必要は無いので、文字列として出す。
                Span label = new();
                AddInlines(label.Inlines, link);
                return label;
            case ContainerInline container:
                Span wrapper = new();
                AddInlines(wrapper.Inlines, container);
                return wrapper;
            default:
                return null;
        }
    }

    /// <summary>
    /// 直前の文字が全角文字か。
    /// </summary>
    private static bool EndsWithWideCharacter(InlineCollection inlines)
    {
        WpfInline? last = inlines.LastInline;

        while (last is Span span)
        {
            last = span.Inlines.LastInline;
        }

        if (last is not Run run || run.Text.Length == 0)
        {
            return false;
        }

        // 和文の範囲（U+3000 以降）だけを全角とみなす。ラテン文字の発音区別符号付き文字は半角扱い。
        return run.Text[^1] >= '\u3000';
    }

    /// <summary>
    /// リソースを引く。見つからなければ既定値を返す。
    /// </summary>
    private T GetResource<T>(string key, T fallback)
        where T : class
    {
        return _resources?[key] as T ?? fallback;
    }

    /// <summary>
    /// 書体を引く。
    /// </summary>
    private FontFamily GetResource(string key, FontFamily fallback)
    {
        return GetResource<FontFamily>(key, fallback);
    }
}

/// <summary>
/// 組み立てた原則文書。
/// </summary>
/// <param name="Document">画面に出す文書。</param>
/// <param name="HeadingParagraphs">見出しと、それを描いた段落の対応。</param>
/// <param name="Folds">折りたたむ章。</param>
/// <param name="OrderedLists">見出しごとの、その直下にある番号付きリスト（規則の一覧）。</param>
public sealed record PolicyRendering(
    FlowDocument Document,
    IReadOnlyDictionary<PolicyHeading, Paragraph> HeadingParagraphs,
    IReadOnlyList<PolicyChapterFold> Folds,
    IReadOnlyDictionary<PolicyHeading, IReadOnlyList<WpfList>> OrderedLists)
{
    /// <summary>
    /// 節の規則（番号付きリストの項目）を引く。節の直下に無ければ、配下の小見出しまで探す。
    ///
    /// 「3.1 規則3」の規則は小見出し 3.1.2 の下にある。番号で引くので、規則 1〜6 と 1〜5 の
    /// ように番号の範囲が違うリストが続いても取り違えない。
    /// </summary>
    /// <param name="headings">文書順の見出し。</param>
    /// <param name="section">節の見出し。</param>
    /// <param name="rule">規則の番号。</param>
    /// <returns>規則の項目。無ければ null。</returns>
    public ListItem? FindRule(IReadOnlyList<PolicyHeading> headings, PolicyHeading section, int rule)
    {
        ArgumentNullException.ThrowIfNull(headings);
        ArgumentNullException.ThrowIfNull(section);

        int start = -1;

        for (int index = 0; index < headings.Count; index++)
        {
            if (ReferenceEquals(headings[index], section))
            {
                start = index;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        for (int index = start; index < headings.Count; index++)
        {
            if (index > start && headings[index].Level <= section.Level)
            {
                break;
            }

            if (!OrderedLists.TryGetValue(headings[index], out IReadOnlyList<WpfList>? lists))
            {
                continue;
            }

            foreach (WpfList list in lists)
            {
                int offset = rule - list.StartIndex;

                if (offset >= 0 && offset < list.ListItems.Count)
                {
                    return list.ListItems.ElementAt(offset);
                }
            }
        }

        return null;
    }
}
