using System.Globalization;

namespace MusicTagAuditor.Core.Inspection;

/// <summary>
/// 検査ルールの根拠となるタグ付け原則の箇所（docs/TAGGING_POLICY.md）。
///
/// 検査ルールは原則を根拠にする（CLAUDE.md）。どの節に基づくかをルール自身に持たせ、
/// 画面のルール ID から原則の該当箇所を開けるようにする（docs/SPEC.md 5.5.2）。
/// </summary>
/// <param name="Section">節番号（例: <c>3.5</c>）。</param>
/// <param name="Rule">節の中の規則の番号。節全体なら null。</param>
public sealed record PolicyBasis(string Section, int? Rule = null)
{
    /// <summary>
    /// 原則での書き方（<c>3.5 規則5</c>）にする。
    /// </summary>
    /// <returns>表示用の文字列。</returns>
    public override string ToString()
    {
        return Rule is int rule
            ? string.Create(CultureInfo.InvariantCulture, $"{Section} 規則{rule}")
            : Section;
    }
}
