using MusicTagger.Core.Models;

namespace MusicTagger.Core.Applying;

/// <summary>
/// 書き込みに失敗したファイル。
/// 1 件の失敗で全体を止めないため、失敗も結果として持ち帰る（docs/SPEC.md 11章）。
/// </summary>
/// <param name="RelativePath">対象ファイル。</param>
/// <param name="Message">失敗の内容。</param>
public sealed record ApplyFailure(string RelativePath, string Message);

/// <summary>
/// 同じフィールドに対して、異なる修正案が同時に選ばれている状態。
///
/// ルール同士の担当範囲がずれると起こりうる。**どちらが正しいか機械的に決められないため
/// 書き込まずに報告する。** 黙ってどちらかを採用すると、意図しない値が入る。
/// </summary>
/// <param name="RelativePath">対象ファイル。</param>
/// <param name="Field">対象フィールド。</param>
/// <param name="Proposals">競合している修正案（ルール ID と修正後の値）。</param>
public sealed record ApplyConflict(
    string RelativePath,
    TagField Field,
    IReadOnlyList<(string RuleId, string AfterText)> Proposals)
{
    /// <summary>表示用の説明。</summary>
    public string Summary =>
        $"{RelativePath} [{Field}] "
        + string.Join(" / ", Proposals.Select(proposal => $"{proposal.RuleId}→「{proposal.AfterText}」"));
}

/// <summary>
/// 適用の結果。
/// </summary>
/// <param name="BackupDirectory">適用直前に取ったスナップショットの場所。</param>
/// <param name="AttemptedFiles">書き込みを試みたファイル数。</param>
/// <param name="SucceededFiles">書き込みに成功したファイル数。</param>
/// <param name="AppliedChanges">書き込んだ項目数。</param>
/// <param name="Failures">書き込みに失敗したファイル。</param>
/// <param name="Mismatches">読み戻して一致しなかった項目。</param>
/// <param name="Conflicts">修正案が競合したため書き込まなかった項目。</param>
public sealed record ApplyResult(
    string BackupDirectory,
    int AttemptedFiles,
    int SucceededFiles,
    int AppliedChanges,
    IReadOnlyList<ApplyFailure> Failures,
    IReadOnlyList<VerificationMismatch> Mismatches,
    IReadOnlyList<ApplyConflict> Conflicts)
{
    /// <summary>
    /// 問題なく完了したか。**不一致が 1 件でもあれば false。**
    /// 書き込めたことと意図した値が入っていることは別である。
    /// </summary>
    public bool IsClean => Failures.Count == 0 && Mismatches.Count == 0 && Conflicts.Count == 0;
}

/// <summary>
/// 適用の進捗。
/// </summary>
/// <param name="Completed">処理を終えたファイル数。</param>
/// <param name="Total">対象ファイル数。</param>
/// <param name="CurrentRelativePath">直近に処理したファイル。</param>
public sealed record ApplyProgress(int Completed, int Total, string CurrentRelativePath);
