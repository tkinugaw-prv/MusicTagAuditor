using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Dictionary;

/// <summary>
/// 辞書に無いために修正案を出せなかった値 1 種。
/// </summary>
/// <param name="Value">タグに入っている値。</param>
/// <param name="Category">辞書のどの種別に入れるべきかの推定。</param>
/// <param name="Count">この値が現れたファイル数。</param>
/// <param name="Fields">検出されたフィールド。</param>
/// <param name="SampleRelativePath">代表 1 件のパス。どのアルバムの話かを掴むために出す。</param>
/// <param name="RuleIds">検出したルール。</param>
public sealed record UnknownValue(
    string Value,
    DictionaryCategory Category,
    int Count,
    IReadOnlyList<TagField> Fields,
    string SampleRelativePath,
    IReadOnlyList<string> RuleIds)
{
    /// <summary>表示用のフィールド名。</summary>
    public string FieldsText => string.Join(" / ", Fields);

    /// <summary>表示用のルール ID。</summary>
    public string RuleIdsText => string.Join(" / ", RuleIds);

    /// <summary>表示用の種別名。</summary>
    public string CategoryText => Category switch
    {
        DictionaryCategory.Composer => DictionaryValidator.CATEGORY_COMPOSER,
        DictionaryCategory.Person => DictionaryValidator.CATEGORY_PERSON,
        _ => DictionaryValidator.CATEGORY_ENSEMBLE,
    };
}

/// <summary>
/// 検査結果から「辞書に無い値」を拾い集める。
///
/// docs/SPEC.md 7.3 が「最も使用頻度が高い」とする導線の入力になる。
/// **明細ではなく値単位でまとめる。** 同じ値が 16 ファイルに散っていても登録作業は 1 回であり、
/// 明細のまま並べると 16 行を目で追うことになる。
/// </summary>
public static class UnknownValueCollector
{
    /// <summary>辞書引きの失敗を報告するルール。</summary>
    private static readonly string[] DICTIONARY_RULE_IDS = ["R-201", "R-202"];

    /// <summary>
    /// 検査結果から未知の値を集める。
    /// </summary>
    /// <param name="changes">検査で得た差分。</param>
    /// <returns>件数の多い順に並べた未知の値。</returns>
    public static IReadOnlyList<UnknownValue> Collect(IEnumerable<TagChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        // 修正案を持つものは辞書に載っているということなので対象外。
        // 保留（HOLD_ERA_UNKNOWN）は辞書ではなく date が埋まれば解決するので、これも対象外。
        IEnumerable<TagChange> targets = changes.Where(change =>
            DICTIONARY_RULE_IDS.Contains(change.RuleId, StringComparer.Ordinal)
            && !change.HasFix
            && change.HoldReason == HoldReason.None
            && change.BeforeValues.Count > 0);

        List<UnknownValue> collected = [];

        // artist と conductor はどちらも人物なので 1 行にまとめる。
        // albumartist は団体なので、同じ文字列でも別行として扱う。
        foreach (var group in targets.GroupBy(change => (
            Value: change.BeforeText,
            Category: DictionaryEditor.SuggestCategory(change.Field))))
        {
            TagChange[] items = [.. group];

            collected.Add(new UnknownValue(
                group.Key.Value,
                group.Key.Category,
                items.Select(change => change.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                [.. items.Select(change => change.Field).Distinct().Order()],
                items[0].RelativePath,
                [.. items.Select(change => change.RuleId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]));
        }

        return [.. collected
            .OrderByDescending(unknown => unknown.Count)
            .ThenBy(unknown => unknown.Value, StringComparer.Ordinal)];
    }
}
