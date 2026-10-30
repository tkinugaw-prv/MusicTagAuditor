using System.Globalization;
using System.Text.RegularExpressions;

namespace MusicTagAuditor.App.Policy;

/// <summary>
/// 画面の文言に書かれた、タグ付け原則への参照 1 つ（例: <c>3.5 規則5</c>）。
/// </summary>
/// <param name="Section">節番号（例: <c>3.5</c>）。章だけなら <c>7</c>。</param>
/// <param name="Rule">規則・原則の番号。節全体を指すなら null。</param>
/// <param name="Supplement">補足を指すときの見出しの接頭辞（例: <c>3.5 補足2</c>）。指さないなら null。</param>
public sealed record PolicyReference(string Section, int? Rule = null, string? Supplement = null);

/// <summary>
/// 文言の一部。参照なら <see cref="Reference"/> を持つ。
/// </summary>
/// <param name="Text">表示する文字列。</param>
/// <param name="Reference">参照。ただの文字列なら null。</param>
public sealed record PolicyTextSegment(string Text, PolicyReference? Reference);

/// <summary>
/// 文言から原則への参照を見つけ、リンクにできる単位に切り分ける（docs/SPEC.md 5.5.1）。
///
/// **節 ID を文言とは別に持たせず、文言そのものから読む。** 別に持つと、文言の番号と ID の
/// 片方だけが直されて食い違う。代わりに読む書式を厳密に絞り、ソース中の参照がすべて
/// 実在の節を指すことをテストで確かめる。
///
/// 読む書式は次の 3 つだけ。番号だけの「3.5」は、SPEC の節番号や日付と区別できないので読まない。
/// <list type="bullet">
/// <item><c>TAGGING_POLICY 3.5</c>（後ろに <c>規則N</c> や <c>補足N</c> が続いてもよい）</item>
/// <item><c>3.5 規則5</c> / <c>3.5 補足2</c>（接頭辞なし。規則か補足が続くものだけ）</item>
/// <item><c>7章 原則4</c></item>
/// </list>
/// <c>・規則N</c> で続く規則は、それぞれ別のリンクにする（<c>3.5 規則2・規則5</c>）。
/// </summary>
public static partial class PolicyReferenceParser
{
    /// <summary>
    /// 文言を切り分ける。参照が無ければ文言全体を 1 つの区切りで返す。
    /// </summary>
    /// <param name="text">文言。</param>
    /// <returns>表示順の区切り。</returns>
    public static IReadOnlyList<PolicyTextSegment> Split(string? text)
    {
        List<PolicyTextSegment> segments = [];

        if (string.IsNullOrEmpty(text))
        {
            return segments;
        }

        int position = 0;

        foreach (Match match in ReferenceRegex().Matches(text))
        {
            if (match.Index > position)
            {
                segments.Add(new PolicyTextSegment(text[position..match.Index], null));
            }

            AddReferenceSegments(match, segments);
            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            segments.Add(new PolicyTextSegment(text[position..], null));
        }

        return segments;
    }

    /// <summary>
    /// 文言に含まれる参照だけを取り出す。
    /// </summary>
    /// <param name="text">文言。</param>
    /// <returns>参照。</returns>
    public static IEnumerable<PolicyReference> FindAll(string? text)
    {
        return Split(text)
            .Where(segment => segment.Reference is not null)
            .Select(segment => segment.Reference!);
    }

    /// <summary>
    /// 一致した参照を、リンク単位の区切りにして書き足す。
    /// </summary>
    private static void AddReferenceSegments(Match match, List<PolicyTextSegment> segments)
    {
        if (match.Groups["chapter"].Success)
        {
            segments.Add(new PolicyTextSegment(
                match.Value,
                new PolicyReference(match.Groups["chapter"].Value, ParseNumber(match.Groups["chapterRule"].Value))));
            return;
        }

        string section = match.Groups["section"].Value;
        Group rules = match.Groups["rule"];
        Group supplement = match.Groups["supplement"];

        if (supplement.Success)
        {
            segments.Add(new PolicyTextSegment(
                match.Value,
                new PolicyReference(section, null, $"{section} 補足{supplement.Value}")));
            return;
        }

        if (rules.Captures.Count == 0)
        {
            segments.Add(new PolicyTextSegment(match.Value, new PolicyReference(section)));
            return;
        }

        // 先頭の規則は節番号と合わせて 1 つのリンクにし、「・規則N」はそれぞれ別のリンクにする。
        Capture first = rules.Captures[0];
        int firstEnd = first.Index + first.Length;
        segments.Add(new PolicyTextSegment(
            match.Value[..(firstEnd - match.Index)],
            new PolicyReference(section, ParseNumber(first.Value))));

        for (int index = 1; index < rules.Captures.Count; index++)
        {
            Capture rule = rules.Captures[index];
            int previousEnd = rules.Captures[index - 1].Index + rules.Captures[index - 1].Length;
            int labelStart = rule.Index - "規則".Length;

            segments.Add(new PolicyTextSegment(match.Value[(previousEnd - match.Index)..(labelStart - match.Index)], null));
            segments.Add(new PolicyTextSegment(
                match.Value[(labelStart - match.Index)..(rule.Index + rule.Length - match.Index)],
                new PolicyReference(section, ParseNumber(rule.Value))));
        }
    }

    /// <summary>
    /// 番号を数値にする。
    /// </summary>
    private static int ParseNumber(string value)
    {
        return int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 参照の書式。
    ///
    /// 接頭辞なしの節番号は、規則か補足が続くときだけ読む。直前が「SPEC 」なら別文書の番号なので読まない。
    /// 補足の番号は省略できる（「3.1 補足」）。
    /// </summary>
    [GeneratedRegex(
        @"(?<chapter>\d+)章 原則(?<chapterRule>\d+)"
        + @"|TAGGING_POLICY (?<section>\d+(?:\.\d+)*)(?: 規則(?<rule>\d+)(?:・規則(?<rule>\d+))*| 補足(?<supplement>\d*))?"
        + @"|(?<!SPEC(?:\.md)? |\d|\.)(?<section>\d+(?:\.\d+)+)(?: 規則(?<rule>\d+)(?:・規則(?<rule>\d+))*| 補足(?<supplement>\d*))")]
    private static partial Regex ReferenceRegex();
}
