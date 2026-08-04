using MusicTagAuditor.Core.Abstractions;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;

namespace MusicTagAuditor.Core.Backup;

/// <summary>
/// スナップショットからタグを復元する。
///
/// **書き込んだあと必ず読み戻して照合する。** 書き込みの成功と、意図した値が入っていることは別である
/// （docs/TAGGING_POLICY.md 7.3 / docs/SPEC.md 9章）。
/// </summary>
public sealed class RestoreService
{
    /// <summary>タグ書き込みの実装。</summary>
    private readonly ITagWriter _tagWriter;

    /// <summary>照合のためのタグ読み取り。</summary>
    private readonly ITagReader _tagReader;

    /// <summary>
    /// 復元サービスを初期化する。
    /// </summary>
    /// <param name="tagWriter">タグ書き込みの実装。</param>
    /// <param name="tagReader">照合に使うタグ読み取りの実装。</param>
    public RestoreService(ITagWriter tagWriter, ITagReader tagReader)
    {
        _tagWriter = tagWriter;
        _tagReader = tagReader;
    }

    /// <summary>
    /// スナップショットと現在の状態を突き合わせ、何が戻るのかを算出する。
    /// </summary>
    /// <param name="backupDirectory">元にしたバックアップフォルダ。</param>
    /// <param name="snapshot">スナップショット。</param>
    /// <param name="current">現在のスキャン結果。</param>
    /// <returns>復元計画。</returns>
    public static RestorePlan BuildPlan(string backupDirectory, TagSnapshot snapshot, ScanResult current)
    {
        Dictionary<string, TrackTags> currentByPath = current.Tracks
            .ToDictionary(track => track.RelativePath, StringComparer.OrdinalIgnoreCase);

        List<RestoreItem> items = [];
        List<string> missing = [];

        foreach (SnapshotTrack snapshotTrack in snapshot.Tracks)
        {
            if (!currentByPath.TryGetValue(snapshotTrack.Path, out TrackTags? currentTrack))
            {
                missing.Add(snapshotTrack.Path);
                continue;
            }

            foreach (TagField field in Enum.GetValues<TagField>())
            {
                string[] snapshotValues = snapshotTrack.Fields.TryGetValue(field.ToString(), out string[]? values)
                    ? values
                    : [];

                IReadOnlyList<string> currentValues = currentTrack.GetValues(field);

                if (!snapshotValues.SequenceEqual(currentValues, StringComparer.Ordinal))
                {
                    items.Add(new RestoreItem(snapshotTrack.Path, field, currentValues, snapshotValues));
                }
            }
        }

        HashSet<string> snapshotPaths = new(
            snapshot.Tracks.Select(track => track.Path),
            StringComparer.OrdinalIgnoreCase);

        string[] added = [.. current.Tracks
            .Select(track => track.RelativePath)
            .Where(path => !snapshotPaths.Contains(path))
            .Order(StringComparer.OrdinalIgnoreCase)];

        return new RestorePlan(
            backupDirectory,
            [.. items.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Field)],
            [.. missing.Order(StringComparer.OrdinalIgnoreCase)],
            added);
    }

    /// <summary>
    /// 選択された項目を書き戻し、読み戻して照合する。
    /// </summary>
    /// <param name="libraryRoot">ライブラリのルート。</param>
    /// <param name="items">復元する項目。<c>IsSelected</c> が true のものだけを対象にする。</param>
    /// <param name="progress">進捗の通知先。不要なら null。</param>
    /// <param name="cancellationToken">キャンセル用トークン。</param>
    /// <returns>復元の結果。</returns>
    public async Task<RestoreResult> ApplyAsync(
        string libraryRoot,
        IReadOnlyList<RestoreItem> items,
        IProgress<RestoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 1 ファイルにつき 1 回の書き込みにまとめる。フィールドごとに開き直すと遅く、失敗の粒度も粗くなる。
        var byFile = items
            .Where(item => item.IsSelected)
            .GroupBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<RestoreFailure> failures = [];
        List<VerificationMismatch> mismatches = [];
        int succeeded = 0;
        int restoredItems = 0;
        int completed = 0;

        foreach (var group in byFile)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string relativePath = group.Key;
            string fullPath = Path.Combine(libraryRoot, relativePath);

            Dictionary<TagField, IReadOnlyList<string>> fields = group
                .ToDictionary(item => item.Field, item => item.SnapshotValues);

            try
            {
                await Task.Run(() => _tagWriter.Write(fullPath, fields), cancellationToken).ConfigureAwait(false);

                // 書き込み成功と、意図した値が入っていることは別。必ず読み戻す。
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
                restoredItems += fields.Count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(new RestoreFailure(relativePath, $"{ex.GetType().Name}: {ex.Message}"));
            }

            progress?.Report(new RestoreProgress(++completed, byFile.Count, relativePath));
        }

        return new RestoreResult(byFile.Count, succeeded, restoredItems, failures, mismatches);
    }
}
