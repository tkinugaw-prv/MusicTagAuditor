using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MusicTagAuditor.App.Policy;

/// <summary>
/// 画面で参照するタグ付け原則（docs/TAGGING_POLICY.md）。
///
/// **本文はビルド時に埋め込んだ原本そのものを読む。** アプリ用の写しを別に持つと、
/// 原本だけが改訂されて画面の原則が古いまま残る。画面に出る原則と、実装の根拠に
/// なっている原則が同じ版であることを、ビルドの仕組みで保証する。
/// </summary>
public sealed partial class PolicyDocument
{
    /// <summary>埋め込みリソース名。csproj の <c>LogicalName</c> と一致させる。</summary>
    public const string RESOURCE_NAME = "MusicTagAuditor.App.TAGGING_POLICY.md";

    /// <summary>
    /// 既定で折りたたむ章の番号。
    ///
    /// 6 章「未確定・未完了の事項」は策定時の作業メモ（件数・残作業）で、
    /// タグを付けるときに引く規則ではない。ただし全文が「実装の唯一の基準」なので、
    /// 省かずに折りたたむだけにする。
    /// </summary>
    public static readonly IReadOnlySet<string> COLLAPSED_CHAPTER_NUMBERS = new HashSet<string>(StringComparer.Ordinal) { "6" };

    /// <summary>Markdown の解析設定。原則は表と入れ子のリストを使う。</summary>
    private static readonly MarkdownPipeline PIPELINE = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .Build();

    /// <summary>
    /// 原則文書を解析する。
    /// </summary>
    /// <param name="markdown">Markdown の本文。</param>
    public PolicyDocument(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        Syntax = Markdown.Parse(markdown, PIPELINE);

        List<PolicyHeading> headings = [];
        string? chapter = null;

        foreach (HeadingBlock block in Syntax.OfType<HeadingBlock>())
        {
            string text = GetPlainText(block.Inline);
            string? number = ExtractNumber(text);

            // 章（##）が変わるたびに所属を切り替える。見出しの番号だけで判定すると、
            // 「##### 3.5 補足3」のような番号を持たない深い見出しの所属を決められない。
            if (block.Level == 2)
            {
                chapter = number;
            }

            headings.Add(new PolicyHeading(block, block.Level, text, number, chapter));
        }

        Headings = headings;
    }

    /// <summary>Markdown の構文木。</summary>
    public MarkdownDocument Syntax { get; }

    /// <summary>文書に現れる順の見出し。表題（#）を含む。</summary>
    public IReadOnlyList<PolicyHeading> Headings { get; }

    /// <summary>
    /// 埋め込みリソースから原則文書を読む。
    /// </summary>
    /// <returns>解析済みの原則文書。</returns>
    /// <exception cref="InvalidOperationException">リソースが埋め込まれていない場合。</exception>
    public static PolicyDocument LoadEmbedded()
    {
        return new PolicyDocument(ReadEmbeddedText());
    }

    /// <summary>
    /// 埋め込みリソースの本文を読む。
    /// </summary>
    /// <returns>Markdown の本文。</returns>
    /// <exception cref="InvalidOperationException">リソースが埋め込まれていない場合。</exception>
    public static string ReadEmbeddedText()
    {
        Assembly assembly = typeof(PolicyDocument).Assembly;

        using Stream stream = assembly.GetManifestResourceStream(RESOURCE_NAME)
            ?? throw new InvalidOperationException($"埋め込みリソース {RESOURCE_NAME} が見つかりません。");
        using StreamReader reader = new(stream);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// 節番号から見出しを引く。
    ///
    /// 同じ番号の見出しが複数あるときは最初のものを返す。「3.5 アルバム名」の後に
    /// 「3.5 補足1」が続くため、本体の節が先に当たる。
    /// </summary>
    /// <param name="number">節番号（例: <c>3.5</c>）。</param>
    /// <returns>見出し。無ければ null。</returns>
    public PolicyHeading? FindSection(string number)
    {
        ArgumentNullException.ThrowIfNull(number);

        return Headings.FirstOrDefault(heading => string.Equals(heading.Number, number, StringComparison.Ordinal));
    }

    /// <summary>
    /// 見出しの先頭から節番号を取り出す。
    /// </summary>
    /// <param name="headingText">見出しの文字列。</param>
    /// <returns>節番号。番号で始まらない見出しなら null。</returns>
    public static string? ExtractNumber(string headingText)
    {
        ArgumentNullException.ThrowIfNull(headingText);

        Match match = HeadingNumberRegex().Match(headingText);

        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// 行内要素を装飾抜きの文字列にする。
    /// </summary>
    /// <param name="inline">行内要素。</param>
    /// <returns>文字列。</returns>
    public static string GetPlainText(ContainerInline? inline)
    {
        if (inline is null)
        {
            return string.Empty;
        }

        System.Text.StringBuilder builder = new();
        AppendPlainText(builder, inline);

        return builder.ToString().Trim();
    }

    /// <summary>
    /// 行内要素の文字列を再帰的に書き足す。
    /// </summary>
    private static void AppendPlainText(System.Text.StringBuilder builder, Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content);
                break;
            case CodeInline code:
                builder.Append(code.Content);
                break;
            case LineBreakInline:
                builder.Append(' ');
                break;
            case ContainerInline container:
                foreach (Inline child in container)
                {
                    AppendPlainText(builder, child);
                }

                break;
        }
    }

    /// <summary>
    /// 見出し先頭の節番号。「1.」「3.1.2」のどちらにも当たる。末尾のピリオドは含めない。
    /// </summary>
    [GeneratedRegex(@"^(\d+(?:\.\d+)*)\.?(?=\s|：|$)")]
    private static partial Regex HeadingNumberRegex();
}

/// <summary>
/// 原則文書の見出し 1 つ。
/// </summary>
/// <param name="Block">構文木上の見出し。</param>
/// <param name="Level">見出しの深さ（<c>#</c> の数）。</param>
/// <param name="Text">見出しの文字列。</param>
/// <param name="Number">節番号。番号で始まらない見出しなら null。</param>
/// <param name="ChapterNumber">所属する章（<c>##</c>）の番号。章より前なら null。</param>
public sealed record PolicyHeading(
    HeadingBlock Block,
    int Level,
    string Text,
    string? Number,
    string? ChapterNumber)
{
    /// <summary>既定で折りたたむ章に属するか。</summary>
    public bool IsInCollapsedChapter =>
        ChapterNumber is not null && PolicyDocument.COLLAPSED_CHAPTER_NUMBERS.Contains(ChapterNumber);
}
