using System.Globalization;
using System.Windows.Documents;

namespace MusicTagAuditor.App.Policy;

/// <summary>
/// 折りたたむ章 1 つ。見出しは常に出し、本文だけを文書へ出し入れする。
///
/// <see cref="FlowDocument"/> の要素には Visibility が無く、Expander も置けない。
/// 本文を 1 つの <see cref="Section"/> にまとめ、開閉のたびに文書へ挿入・除去する。
/// </summary>
public sealed class PolicyChapterFold
{
    /// <summary>開閉リンクの前に置く説明。</summary>
    private readonly Run _description;

    /// <summary>開閉リンク。</summary>
    private readonly Hyperlink _link;

    /// <summary>章に含まれる節（###）の数。</summary>
    private readonly int _sectionCount;

    /// <summary>
    /// 折りたたむ章を作る。
    /// </summary>
    /// <param name="heading">章の見出し。</param>
    /// <param name="headingParagraph">章の見出しを描いた段落。</param>
    /// <param name="togglePanel">見出しの直後に置く開閉欄。</param>
    /// <param name="description">開閉欄の説明。</param>
    /// <param name="link">開閉リンク。</param>
    /// <param name="body">章の本文。</param>
    /// <param name="sectionCount">章に含まれる節（###）の数。</param>
    public PolicyChapterFold(
        PolicyHeading heading,
        Paragraph headingParagraph,
        Paragraph togglePanel,
        Run description,
        Hyperlink link,
        Section body,
        int sectionCount)
    {
        Heading = heading;
        HeadingParagraph = headingParagraph;
        TogglePanel = togglePanel;
        _description = description;
        _link = link;
        Body = body;
        _sectionCount = sectionCount;

        _link.Click += (_, _) => Toggle();
        UpdateToggleText();
    }

    /// <summary>開閉が変わったときに通知する。</summary>
    public event EventHandler? ExpandedChanged;

    /// <summary>章の見出し。</summary>
    public PolicyHeading Heading { get; }

    /// <summary>章の見出しを描いた段落。</summary>
    public Paragraph HeadingParagraph { get; }

    /// <summary>見出しの直後に置く開閉欄。</summary>
    public Paragraph TogglePanel { get; }

    /// <summary>章の本文。閉じている間は文書に含まれない。</summary>
    public Section Body { get; }

    /// <summary>開いているか。</summary>
    public bool IsExpanded { get; private set; }

    /// <summary>
    /// 本文を開く。開いていれば何もしない。
    /// </summary>
    public void Expand()
    {
        if (IsExpanded)
        {
            return;
        }

        TogglePanel.SiblingBlocks.InsertAfter(TogglePanel, Body);
        IsExpanded = true;
        UpdateToggleText();
        ExpandedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 本文を閉じる。閉じていれば何もしない。
    /// </summary>
    public void Collapse()
    {
        if (!IsExpanded)
        {
            return;
        }

        TogglePanel.SiblingBlocks.Remove(Body);
        IsExpanded = false;
        UpdateToggleText();
        ExpandedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 開閉を切り替える。
    /// </summary>
    public void Toggle()
    {
        if (IsExpanded)
        {
            Collapse();
        }
        else
        {
            Expand();
        }
    }

    /// <summary>
    /// 開閉欄の文言を状態に合わせる。
    /// </summary>
    private void UpdateToggleText()
    {
        _description.Text = IsExpanded
            ? string.Empty
            : string.Create(
                CultureInfo.CurrentCulture,
                $"この章（{_sectionCount} 節）は策定時に残った未確定事項と作業の記録で、タグを付けるときに引く規則ではないため折りたたんでいます。");

        _link.Inlines.Clear();
        _link.Inlines.Add(new Run(IsExpanded ? "▲ 折りたたむ" : " ▼ 表示する"));
    }
}
