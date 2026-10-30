using System.Windows;

namespace MusicTagAuditor.App.Policy;

/// <summary>
/// 原則ウィンドウの目次 1 行。
/// </summary>
/// <param name="Heading">対応する見出し。</param>
public sealed record PolicyOutlineItem(PolicyHeading Heading)
{
    /// <summary>目次に載せる最も深い見出し（#####）。補足は判断理由と実測で、探したくなることがある。</summary>
    public const int MAX_LEVEL = 5;

    /// <summary>目次に出す文字列。</summary>
    public string Text => Heading.Text;

    /// <summary>章（##）の行か。章は太字で区切りにする。</summary>
    public bool IsChapter => Heading.Level == 2;

    /// <summary>既定で折りたたむ章に属するか。目次でも控えめに出す。</summary>
    public bool IsInCollapsedChapter => Heading.IsInCollapsedChapter;

    /// <summary>深さに応じた字下げ。</summary>
    public Thickness Indent => new((Heading.Level - 2) * 14, IsChapter ? 6 : 0, 0, 0);

    /// <summary>
    /// 原則文書から目次を作る。表題（#）は載せない。
    /// </summary>
    /// <param name="document">原則文書。</param>
    /// <returns>目次。</returns>
    public static IReadOnlyList<PolicyOutlineItem> CreateOutline(PolicyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return [.. document.Headings
            .Where(heading => heading.Level is >= 2 and <= MAX_LEVEL)
            .Select(heading => new PolicyOutlineItem(heading))];
    }
}
