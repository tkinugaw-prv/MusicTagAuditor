using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Inspection;

/// <summary>
/// 検査ルール 1 つ。ルール一覧は docs/SPEC.md 6.1 を参照。
/// </summary>
public interface IInspectionRule
{
    /// <summary>ルール ID（例: <c>R-201</c>）。</summary>
    string Id { get; }

    /// <summary>重大度。</summary>
    Severity Severity { get; }

    /// <summary>一覧に出す説明。</summary>
    string Description { get; }

    /// <summary>既定で有効かどうか。誤検出が多いものは無効にしておく。</summary>
    bool IsEnabledByDefault => true;

    /// <summary>
    /// 検査を実行して修正案を返す。
    /// </summary>
    /// <param name="context">ライブラリ全体の文脈。</param>
    /// <returns>検出した修正案。</returns>
    IEnumerable<TagChange> Inspect(InspectionContext context);
}

/// <summary>
/// ルールの実行結果をまとめたもの。
/// </summary>
/// <param name="RuleId">ルール ID。</param>
/// <param name="Severity">重大度。</param>
/// <param name="Description">説明。</param>
/// <param name="Changes">検出した修正案。</param>
public sealed record RuleResult(
    string RuleId,
    Severity Severity,
    string Description,
    IReadOnlyList<TagChange> Changes)
{
    /// <summary>自動修正できる件数。</summary>
    public int FixableCount => Changes.Count(change => change.HasFix);

    /// <summary>保留になった件数。</summary>
    public int HoldCount => Changes.Count(change => change.HoldReason != HoldReason.None);
}

/// <summary>
/// 検査全体の結果。
/// </summary>
/// <param name="Results">ルールごとの結果。ID 順。</param>
/// <param name="Elapsed">検査に要した時間。</param>
public sealed record InspectionResult(IReadOnlyList<RuleResult> Results, TimeSpan Elapsed)
{
    /// <summary>検出した修正案の総数。</summary>
    public int TotalChanges => Results.Sum(result => result.Changes.Count);

    /// <summary>すべての修正案を平坦に返す。</summary>
    public IEnumerable<TagChange> AllChanges => Results.SelectMany(result => result.Changes);
}
