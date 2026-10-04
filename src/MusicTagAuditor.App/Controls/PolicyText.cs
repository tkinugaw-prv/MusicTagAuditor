using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MusicTagAuditor.App.Policy;

namespace MusicTagAuditor.App.Controls;

/// <summary>
/// TextBlock の文言のうち、タグ付け原則への参照（<c>3.5 規則5</c> など）をリンクにする添付プロパティ
/// （docs/SPEC.md 5.5.1）。
///
/// <c>Text</c> の代わりに <c>ctl:PolicyText.Text</c> を指定する。参照を押すと原則ウィンドウが
/// その箇所を開く。文言そのものは変えないので、参照が無い文言はそのまま出る。
/// </summary>
public static class PolicyText
{
    /// <summary>表示する文言。</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(PolicyText),
            new PropertyMetadata(null, OnTextChanged));

    /// <summary>
    /// 文言を取得する。
    /// </summary>
    /// <param name="element">対象の要素。</param>
    /// <returns>文言。</returns>
    public static string? GetText(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return (string?)element.GetValue(TextProperty);
    }

    /// <summary>
    /// 文言を設定する。
    /// </summary>
    /// <param name="element">対象の要素。</param>
    /// <param name="value">文言。</param>
    public static void SetText(DependencyObject element, string? value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(TextProperty, value);
    }

    /// <summary>
    /// 文言が変わったら、参照をリンクにして並べ直す。
    /// </summary>
    private static void OnTextChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock textBlock)
        {
            return;
        }

        textBlock.Inlines.Clear();

        foreach (PolicyTextSegment segment in PolicyReferenceParser.Split(e.NewValue as string))
        {
            textBlock.Inlines.Add(segment.Reference is PolicyReference reference
                ? CreateLink(textBlock, segment.Text, reference)
                : new Run(segment.Text));
        }
    }

    /// <summary>
    /// 参照のリンクを作る。
    /// </summary>
    private static Hyperlink CreateLink(TextBlock owner, string text, PolicyReference reference)
    {
        Hyperlink link = new(new Run(text))
        {
            ToolTip = string.Create(CultureInfo.CurrentCulture, $"タグ付け原則の {Describe(reference)} を開きます"),
        };

        if (owner.TryFindResource("AccentBrush") is Brush accent)
        {
            link.Foreground = accent;
        }

        link.Click += (_, _) =>
        {
            if (Application.Current?.MainWindow is Window mainWindow)
            {
                PolicyWindow.ShowShared(mainWindow, reference);
            }
        };

        return link;
    }

    /// <summary>
    /// 参照先を短く言い表す。
    /// </summary>
    private static string Describe(PolicyReference reference)
    {
        if (reference.Supplement is string supplement)
        {
            return supplement;
        }

        return reference.Rule is int rule
            ? string.Create(CultureInfo.CurrentCulture, $"{reference.Section} の {rule} 番目の規則")
            : reference.Section;
    }
}
