using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Applying;

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
///
/// **例外は手編集**（段階 6）。人間が明示的に入れた値のほうが強いので、
/// 競合していても手編集の値を書き込む。ただし競合そのものは握りつぶさず、
/// どの案を捨てたのかが分かる形で報告する。
/// </summary>
/// <param name="RelativePath">対象ファイル。</param>
/// <param name="Field">対象フィールド。</param>
/// <param name="Proposals">競合している修正案（ルール ID と修正後の値）。</param>
/// <param name="AdoptedValue">採用した値。書き込まなかった場合は null。</param>
public sealed record ApplyConflict(
    string RelativePath,
    TagField Field,
    IReadOnlyList<(string RuleId, string AfterText)> Proposals,
    string? AdoptedValue = null)
{
    /// <summary>競合を解消して書き込んだか。</summary>
    public bool IsResolved => AdoptedValue is not null;

    /// <summary>表示用の説明。</summary>
    public string Summary =>
        $"{RelativePath} [{Field}] "
        + string.Join(" / ", Proposals.Select(proposal => $"{proposal.RuleId}→「{proposal.AfterText}」"))
        + (IsResolved ? $" — 手編集の「{AdoptedValue}」を採用した" : " — 書き込まなかった");
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
    ///
    /// 手編集で解消した競合は、捨てた案を利用者に知らせる必要があるので「要確認」に数える。
    /// </summary>
    public bool IsClean => Failures.Count == 0 && Mismatches.Count == 0 && Conflicts.Count == 0;

    /// <summary>
    /// <paramref name="targets"/> のうち、完全に成功した (ファイル, フィールド) の組を返す。
    ///
    /// 書き込みに失敗したファイルは全フィールドを除外する。読み戻し照合の不一致・競合になった
    /// 組も除外する（手編集で解消済みの競合を含む。捨てた案があったことを利用者が確認できるよう
    /// 検査結果に残すため）。呼び出し側はこの結果を検査結果から取り除く用途に使う想定。
    /// </summary>
    public IReadOnlySet<TagChangeKey> GetSucceededFields(IEnumerable<TagChange> targets)
    {
        HashSet<string> failedFiles = new(
            Failures.Select(failure => failure.RelativePath),
            StringComparer.OrdinalIgnoreCase);

        HashSet<TagChangeKey> blocked =
        [
            .. Mismatches.Select(mismatch => TagChangeKey.Of(mismatch.RelativePath, mismatch.Field)),
            .. Conflicts.Select(conflict => TagChangeKey.Of(conflict.RelativePath, conflict.Field)),
        ];

        return new HashSet<TagChangeKey>(
            targets
                .Where(change => !failedFiles.Contains(change.RelativePath))
                .Select(TagChangeKey.From)
                .Where(key => !blocked.Contains(key)));
    }
}

/// <summary>
/// 適用の進捗。
/// </summary>
/// <param name="Completed">処理を終えたファイル数。</param>
/// <param name="Total">対象ファイル数。</param>
/// <param name="CurrentRelativePath">直近に処理したファイル。</param>
public sealed record ApplyProgress(int Completed, int Total, string CurrentRelativePath);
