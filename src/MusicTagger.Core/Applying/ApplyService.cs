using MusicTagger.Core.Abstractions;
using MusicTagger.Core.Backup;
using MusicTagger.Core.Editing;
using MusicTagger.Core.Models;
using MusicTagger.Core.Scanning;

namespace MusicTagger.Core.Applying;

/// <summary>
/// 検査で選ばれた修正案をファイルへ書き込む。
///
/// docs/SPEC.md 9章の適用フローを実装する。守るべき点が 3 つある。
///
/// 1. **適用の直前に必ずスナップショットを取る。** 利用者が明示的にバックアップを取っていなくても
/// 2. **書き込んだ全項目を読み戻して照合する。** 工程 6 を省略しない
/// 3. **1 件の失敗で全体を止めない。** 失敗一覧を持ち帰る
/// </summary>
public sealed class ApplyService
{
    /// <summary>タグ書き込みの実装。</summary>
    private readonly ITagWriter _tagWriter;

    /// <summary>照合のためのタグ読み取り。</summary>
    private readonly ITagReader _tagReader;

    /// <summary>スナップショットの取得。</summary>
    private readonly SnapshotService _snapshotService;

    /// <summary>
    /// 適用サービスを初期化する。
    /// </summary>
    /// <param name="tagWriter">タグ書き込みの実装。</param>
    /// <param name="tagReader">照合に使うタグ読み取りの実装。</param>
    /// <param name="snapshotService">スナップショットの取得。</param>
    public ApplyService(ITagWriter tagWriter, ITagReader tagReader, SnapshotService snapshotService)
    {
        _tagWriter = tagWriter;
        _tagReader = tagReader;
        _snapshotService = snapshotService;
    }

    /// <summary>
    /// 選択された修正案を適用する。
    /// </summary>
    /// <param name="scan">適用前のスキャン結果。スナップショットの取得元になる。</param>
    /// <param name="changes">検査で得た修正案。<c>IsSelected</c> かつ修正値を持つものだけを適用する。</param>
    /// <param name="note">スナップショットに残す補足。</param>
    /// <param name="portableLibraryPath">復元スクリプトが使うタグライブラリの場所。</param>
    /// <param name="progress">進捗の通知先。不要なら null。</param>
    /// <param name="cancellationToken">キャンセル用トークン。</param>
    /// <returns>適用の結果。</returns>
    public async Task<ApplyResult> ApplyAsync(
        ScanResult scan,
        IEnumerable<TagChange> changes,
        string? note = null,
        string? portableLibraryPath = null,
        IProgress<ApplyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        TagChange[] targets = [.. changes.Where(change => change.IsSelected && change.HasFix)];

        // 1 ファイルにつき 1 回の書き込みにまとめる。
        var byFile = targets
            .GroupBy(change => change.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // **書き込む前にスナップショットを取る。** ここを後回しにすると巻き戻せない。
        string backupDirectory = _snapshotService.Create(
            scan,
            SnapshotReason.BeforeApply,
            note ?? BuildNote(targets),
            portableLibraryPath);

        List<ApplyFailure> failures = [];
        List<VerificationMismatch> mismatches = [];
        List<ApplyConflict> conflicts = [];
        int succeeded = 0;
        int applied = 0;
        int completed = 0;

        foreach (var group in byFile)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relativePath = group.Key;
            string fullPath = Path.Combine(scan.LibraryRoot, relativePath);

            Dictionary<TagField, IReadOnlyList<string>> fields = BuildFields(group, conflicts);

            if (fields.Count == 0)
            {
                progress?.Report(new ApplyProgress(++completed, byFile.Count, relativePath));
                continue;
            }

            try
            {
                await Task.Run(() => _tagWriter.Write(fullPath, fields), cancellationToken).ConfigureAwait(false);

                // 工程 6。書き込めたことと、意図した値が入っていることは別。
                TrackTags written = await Task
                    .Run(() => _tagReader.Read(fullPath, relativePath), cancellationToken)
                    .ConfigureAwait(false);

                foreach ((TagField field, IReadOnlyList<string> expected) in fields)
                {
                    IReadOnlyList<string> actual = written.GetValues(field);

                    if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
                    {
                        mismatches.Add(new VerificationMismatch(relativePath, field, expected, actual));
                    }
                }

                succeeded++;
                applied += fields.Count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(new ApplyFailure(relativePath, $"{ex.GetType().Name}: {ex.Message}"));
            }

            progress?.Report(new ApplyProgress(++completed, byFile.Count, relativePath));
        }

        return new ApplyResult(
            backupDirectory,
            byFile.Count,
            succeeded,
            applied,
            failures,
            mismatches,
            conflicts);
    }

    /// <summary>
    /// 1 ファイル分の書き込み内容を組み立てる。
    /// 同じフィールドに異なる修正案が選ばれている場合は書き込まず、競合として報告する。
    ///
    /// **例外は手編集。** 人間が明示的に入れた値のほうが強いので、競合していても手編集を採用する。
    /// 競合そのものは報告し続ける。捨てた案が何だったかを利用者が知る必要があるため。
    /// </summary>
    private static Dictionary<TagField, IReadOnlyList<string>> BuildFields(
        IGrouping<string, TagChange> group,
        List<ApplyConflict> conflicts)
    {
        Dictionary<TagField, IReadOnlyList<string>> fields = [];

        foreach (var byField in group.GroupBy(change => change.Field))
        {
            TagChange[] proposals = [.. byField];

            // 値が同じなら競合ではない。別々のルールが同じ結論に達しただけ。
            string[] distinct = [.. proposals.Select(change => change.AfterText).Distinct(StringComparer.Ordinal)];

            if (distinct.Length <= 1)
            {
                fields[byField.Key] = proposals[0].AfterValues;
                continue;
            }

            TagChange[] manual = [.. proposals.Where(change => change.RuleId == ManualEditConst.RULE_ID)];

            // 手編集どうしが食い違うことは無い（1 ファイル 1 フィールドに 1 件しか持てない）。
            // 万一 2 件以上来たら機械的に決められないので、通常どおり書き込まずに報告する。
            if (manual.Length != 1)
            {
                conflicts.Add(new ApplyConflict(
                    group.Key,
                    byField.Key,
                    [.. proposals.Select(change => (change.RuleId, change.AfterText))]));

                continue;
            }

            fields[byField.Key] = manual[0].AfterValues;

            conflicts.Add(new ApplyConflict(
                group.Key,
                byField.Key,
                [.. proposals.Select(change => (change.RuleId, change.AfterText))],
                manual[0].AfterText));
        }

        return fields;
    }

    /// <summary>
    /// スナップショットに残す補足を組み立てる。何のための適用だったかを後から追えるようにする。
    /// </summary>
    private static string BuildNote(IReadOnlyList<TagChange> targets)
    {
        if (targets.Count == 0)
        {
            return "適用対象なし";
        }

        string rules = string.Join(
            " / ",
            targets.GroupBy(change => change.RuleId)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key} {group.Count()}件"));

        return $"適用前（{targets.Count} 項目: {rules}）";
    }
}
