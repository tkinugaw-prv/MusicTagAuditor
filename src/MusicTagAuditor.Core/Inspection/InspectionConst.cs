using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Inspection;

/// <summary>
/// 検査エンジンで使う定数。
/// docs/llm_guideline.md の規約により、定数は FULL_CAPITAL で定義する。
/// </summary>
public static class InspectionConst
{
    /// <summary>
    /// 検査の対象にするフィールド。全フィールドを走査するルールはこれを回すこと。
    ///
    /// <c>Enum.GetValues&lt;TagField&gt;()</c> を直接回してはならない。自由記述のフィールドは
    /// 正規形を定めないため検査できず（docs/TAGGING_POLICY.md 2.4）、走査に混ざると
    /// 誤検出になる。<c>comment</c> は句読点として <c>;</c> を含みうるので R-205 が顕著。
    ///
    /// 「全体から自由記述を引く」向きで導出しているのは、フィールドを足したときに
    /// 既定で検査対象になるようにするため。明示列挙にすると、足し忘れたフィールドが
    /// 黙って検査対象外になり、検査が手薄になったことに誰も気づけない。
    /// </summary>
    public static readonly IReadOnlyList<TagField> INSPECTED_FIELDS =
        [.. Enum.GetValues<TagField>().Where(field => !TagFieldConst.IsFreeText(field))];
}
