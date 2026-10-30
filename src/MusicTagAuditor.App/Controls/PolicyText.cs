using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MusicTagAuditor.App.Policy;
using MusicTagAuditor.Core.Inspection;

namespace MusicTagAuditor.App.Controls;

/// <summary>
/// TextBlock の文言のうち、タグ付け原則への参照（<c>3.5 規則5</c> など）をリンクにする添付プロパティ
/// （docs/SPEC.md 5.5.1）。
///
/// <c>Text</c> の代わりに <c>ctl:PolicyText.Text</c> を指定する。参照を押すと原則ウィンドウが
/// その箇所を開く。文言そのものは変えないので、参照が無い文言はそのまま出る。
///
/// ルール ID を並べる欄には <c>ctl:PolicyText.RuleIds</c> を使う。ID の後ろに、そのルールの
/// 根拠の節をリンクで添える（docs/SPEC.md 5.5.2）。
/// </summary>
public static class PolicyText
{
    /// <summary>ルール ID から根拠を引く表。ルールの定義は起動中に変わらないので 1 度だけ作る。</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<PolicyBasis>>> BASES_BY_RULE_ID = new(
        () => InspectionEngine.CreateDefaultRules().ToDictionary(rule => rule.Id, rule => rule.PolicyBases, StringComparer.Ordinal));

    /// <summary>ルール ID と根拠の間の空き。</summary>
    private const string ID_BASIS_GAP = "  ";

    /// <summary>ルールが複数並ぶときの区切り。</summary>
    private const string RULE_SEPARATOR = "   ";

    /// <summary>表示するルール ID。1 つなら文字列、複数なら文字列の列。</summary>
    public static readonly DependencyProperty RuleIdsProperty =
        DependencyProperty.RegisterAttached(
            "RuleIds",
            typeof(object),
            typeof(PolicyText),
            new PropertyMetadata(null, OnRuleIdsChanged));

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
    /// 表示するルール ID を取得する。
    /// </summary>
    /// <param name="element">対象の要素。</param>
    /// <returns>ルール ID。</returns>
    public static object? GetRuleIds(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return element.GetValue(RuleIdsProperty);
    }

    /// <summary>
    /// 表示するルール ID を設定する。
    /// </summary>
    /// <param name="element">対象の要素。</param>
    /// <param name="value">ルール ID。1 つなら文字列、複数なら文字列の列。</param>
    public static void SetRuleIds(DependencyObject element, object? value)
    {
        ArgumentNullException.ThrowIfNull(element);

        element.SetValue(RuleIdsProperty, value);
    }

    /// <summary>
    /// ルール ID の根拠を引く。表に無い ID（手編集など）は空を返す。
    /// </summary>
    /// <param name="ruleId">ルール ID。</param>
    /// <returns>根拠。</returns>
    public static IReadOnlyList<PolicyBasis> GetBases(string ruleId)
    {
        ArgumentNullException.ThrowIfNull(ruleId);

        return BASES_BY_RULE_ID.Value.TryGetValue(ruleId, out IReadOnlyList<PolicyBasis>? bases) ? bases : [];
    }

    /// <summary>
    /// ルール ID が変わったら、ID と根拠のリンクを並べ直す。
    ///
    /// 同じ節の規則は「3.5 規則5・規則6」のように節番号を 1 度だけ書き、規則ごとにリンクを分ける。
    /// 根拠が複数の節にわたるルールは「5.2 / 5.3」のように節ごとにリンクを分ける。
    /// </summary>
    private static void OnRuleIdsChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock textBlock)
        {
            return;
        }

        textBlock.Inlines.Clear();

        string[] ruleIds = e.NewValue switch
        {
            string single => [single],
            IEnumerable<string> many => [.. many],
            _ => [],
        };

        for (int index = 0; index < ruleIds.Length; index++)
        {
            if (index > 0)
            {
                textBlock.Inlines.Add(new Run(RULE_SEPARATOR));
            }

            textBlock.Inlines.Add(new Run(ruleIds[index]));

            IReadOnlyList<PolicyBasis> bases = GetBases(ruleIds[index]);

            if (bases.Count > 0)
            {
                textBlock.Inlines.Add(new Run(ID_BASIS_GAP));
                AddBasisLinks(textBlock, bases);
            }
        }
    }

    /// <summary>
    /// 根拠をリンクにして足す。
    /// </summary>
    private static void AddBasisLinks(TextBlock textBlock, IReadOnlyList<PolicyBasis> bases)
    {
        string? previousSection = null;

        foreach (PolicyBasis basis in bases)
        {
            bool sameSection = basis.Section == previousSection && basis.Rule is not null;

            if (previousSection is not null)
            {
                textBlock.Inlines.Add(new Run(sameSection ? "・" : " / "));
            }

            string label = sameSection ? $"規則{basis.Rule}" : basis.ToString();
            textBlock.Inlines.Add(CreateLink(textBlock, label, new PolicyReference(basis.Section, basis.Rule)));
            previousSection = basis.Section;
        }
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
