using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Backup;

/// <summary>
/// 復元で戻す 1 項目。差分プレビューの 1 行に対応する。
/// </summary>
/// <param name="RelativePath">ライブラリルートからの相対パス。</param>
/// <param name="Field">対象フィールド。</param>
/// <param name="CurrentValues">現在ファイルに入っている値。</param>
/// <param name="SnapshotValues">スナップショットに記録されている値。これに戻す。</param>
public sealed record RestoreItem(
    string RelativePath,
    TagField Field,
    IReadOnlyList<string> CurrentValues,
    IReadOnlyList<string> SnapshotValues)
{
    /// <summary>復元するかどうか。既定で復元対象にする。</summary>
    public bool IsSelected { get; set; } = true;

    /// <summary>表示用の現在値。</summary>
    public string CurrentText => string.Join(TrackTags.VALUE_JOIN_SEPARATOR, CurrentValues);

    /// <summary>表示用の復元後の値。</summary>
    public string SnapshotText => string.Join(TrackTags.VALUE_JOIN_SEPARATOR, SnapshotValues);
}

/// <summary>
/// 復元計画。**適用前に必ずこれを人間に見せる**（docs/SPEC.md 8.3）。
/// </summary>
/// <param name="BackupDirectory">元にしたバックアップフォルダ。</param>
/// <param name="Items">戻す項目。</param>
/// <param name="MissingFiles">スナップショットにはあるが現在は存在しないファイル。</param>
/// <param name="AddedFiles">現在は存在するがスナップショットには無いファイル。復元では触らない。</param>
public sealed record RestorePlan(
    string BackupDirectory,
    IReadOnlyList<RestoreItem> Items,
    IReadOnlyList<string> MissingFiles,
    IReadOnlyList<string> AddedFiles);

/// <summary>
/// 復元に失敗したファイル。
/// </summary>
/// <param name="RelativePath">対象ファイル。</param>
/// <param name="Message">失敗の内容。</param>
public sealed record RestoreFailure(string RelativePath, string Message);

/// <summary>
/// 復元の結果。
/// </summary>
/// <param name="AttemptedFiles">書き込みを試みたファイル数。</param>
/// <param name="SucceededFiles">書き込みに成功したファイル数。</param>
/// <param name="RestoredItems">戻した項目数。</param>
/// <param name="Failures">書き込みに失敗したファイル。</param>
/// <param name="Mismatches">読み戻して一致しなかった項目。</param>
public sealed record RestoreResult(
    int AttemptedFiles,
    int SucceededFiles,
    int RestoredItems,
    IReadOnlyList<RestoreFailure> Failures,
    IReadOnlyList<VerificationMismatch> Mismatches);

/// <summary>
/// 復元の進捗。
/// </summary>
/// <param name="Completed">処理を終えたファイル数。</param>
/// <param name="Total">対象ファイル数。</param>
/// <param name="CurrentRelativePath">直近に処理したファイル。</param>
public sealed record RestoreProgress(int Completed, int Total, string CurrentRelativePath);
