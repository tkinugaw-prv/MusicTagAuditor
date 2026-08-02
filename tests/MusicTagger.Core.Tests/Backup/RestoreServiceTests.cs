using MusicTagger.Core.Abstractions;
using MusicTagger.Core.Backup;
using MusicTagger.Core.Models;
using MusicTagger.Core.Scanning;

namespace MusicTagger.Core.Tests.Backup;

/// <summary>
/// 復元のテスト。
/// **書き込んだあと読み戻して照合する**工程が動いていることを重点的に確認する
/// （docs/TAGGING_POLICY.md 7.3）。
/// </summary>
public sealed class RestoreServiceTests
{
    /// <summary>テストで使うライブラリルート。実ファイルは作らない。</summary>
    private const string LIBRARY_ROOT = @"D:\Library";

    /// <summary>
    /// 変化したフィールドだけが復元計画に載ることを確認する。
    /// </summary>
    [Fact]
    public void BuildsPlanForChangedFieldsOnly()
    {
        TagSnapshot snapshot = BuildSnapshot(
        [
            ("01.m4a", TagField.Composer, ["Anton Bruckner"]),
            ("01.m4a", TagField.Genre, ["Classic"]),
        ]);

        ScanResult current = BuildScan(
        [
            ("01.m4a", TagField.Composer, ["Btuckner"]),
            ("01.m4a", TagField.Genre, ["Classic"]),
        ]);

        RestorePlan plan = RestoreService.BuildPlan("backup_1", snapshot, current);

        RestoreItem item = Assert.Single(plan.Items);
        Assert.Equal(TagField.Composer, item.Field);
        Assert.Equal("Btuckner", item.CurrentText);
        Assert.Equal("Anton Bruckner", item.SnapshotText);
    }

    /// <summary>
    /// スナップショット以降に消えたファイルと増えたファイルを区別して報告することを確認する。
    /// 復元前に「何が戻るのか」を示すには、戻せないものも示す必要がある。
    /// </summary>
    [Fact]
    public void ReportsMissingAndAddedFiles()
    {
        TagSnapshot snapshot = BuildSnapshot([("消えた.m4a", TagField.Composer, ["Anton Bruckner"])]);
        ScanResult current = BuildScan([("増えた.m4a", TagField.Composer, ["Gustav Mahler"])]);

        RestorePlan plan = RestoreService.BuildPlan("backup_1", snapshot, current);

        Assert.Equal("消えた.m4a", Assert.Single(plan.MissingFiles));
        Assert.Equal("増えた.m4a", Assert.Single(plan.AddedFiles));
        Assert.Empty(plan.Items);
    }

    /// <summary>
    /// タグが増えた場合、スナップショット時点の「未設定」に戻せることを確認する。
    /// </summary>
    [Fact]
    public void PlansRemovalWhenSnapshotHadNoValue()
    {
        TagSnapshot snapshot = BuildSnapshot([("01.m4a", TagField.Composer, ["Anton Bruckner"])]);

        ScanResult current = BuildScan(
        [
            ("01.m4a", TagField.Composer, ["Anton Bruckner"]),
            ("01.m4a", TagField.Conductor, ["間違って入れた値"]),
        ]);

        RestorePlan plan = RestoreService.BuildPlan("backup_1", snapshot, current);

        RestoreItem item = Assert.Single(plan.Items);
        Assert.Equal(TagField.Conductor, item.Field);
        Assert.Empty(item.SnapshotValues);
    }

    /// <summary>
    /// 複数値と、<c>;</c> を含む 1 値を別物として扱うことを確認する。
    /// 表示上は同じ文字列になるため、ここを取り違えると差分が消える。
    /// </summary>
    [Fact]
    public void DistinguishesSplitValuesFromSemicolonValue()
    {
        TagSnapshot snapshot = BuildSnapshot(
            [("01.m4a", TagField.AlbumArtist, ["Peter Pears(T); Hermann Prey(BR)"])]);

        ScanResult current = BuildScan(
            [("01.m4a", TagField.AlbumArtist, ["Peter Pears(T)", "Hermann Prey(BR)"])]);

        RestorePlan plan = RestoreService.BuildPlan("backup_1", snapshot, current);

        RestoreItem item = Assert.Single(plan.Items);
        Assert.Equal(item.CurrentText, item.SnapshotText);
        Assert.NotEqual(item.CurrentValues.Count, item.SnapshotValues.Count);
    }

    /// <summary>
    /// 選択された項目だけを書き戻すことを確認する。
    /// </summary>
    [Fact]
    public async Task WritesOnlySelectedItems()
    {
        RecordingTagWriter writer = new();
        RestoreService service = new(writer, new EchoTagReader(writer));

        RestoreItem selected = new("01.m4a", TagField.Composer, ["Btuckner"], ["Anton Bruckner"]);
        RestoreItem skipped = new("01.m4a", TagField.Genre, ["Classical"], ["Classic"]) { IsSelected = false };

        RestoreResult result = await service.ApplyAsync(LIBRARY_ROOT, [selected, skipped]);

        Assert.Equal(1, result.SucceededFiles);
        Assert.Equal(1, result.RestoredItems);
        Assert.Empty(result.Mismatches);

        Dictionary<TagField, IReadOnlyList<string>> written = writer.Writes.Single().Value;
        Assert.Equal(["Anton Bruckner"], written[TagField.Composer]);
        Assert.DoesNotContain(TagField.Genre, written.Keys);
    }

    /// <summary>
    /// 1 ファイルの書き込み失敗で全体を止めないことを確認する。
    /// </summary>
    [Fact]
    public async Task ContinuesAfterWriteFailure()
    {
        RecordingTagWriter writer = new() { FailingPath = "壊れた.m4a" };
        RestoreService service = new(writer, new EchoTagReader(writer));

        RestoreResult result = await service.ApplyAsync(
            LIBRARY_ROOT,
            [
                new RestoreItem("01.m4a", TagField.Composer, [], ["Anton Bruckner"]),
                new RestoreItem("壊れた.m4a", TagField.Composer, [], ["Gustav Mahler"]),
                new RestoreItem("02.m4a", TagField.Composer, [], ["Johannes Brahms"]),
            ]);

        Assert.Equal(3, result.AttemptedFiles);
        Assert.Equal(2, result.SucceededFiles);
        Assert.Equal("壊れた.m4a", Assert.Single(result.Failures).RelativePath);
    }

    /// <summary>
    /// **書き込めたのに値が違う**場合を検出することを確認する。
    /// 書き込み成功と、意図した値が入っていることは別である。
    /// </summary>
    [Fact]
    public async Task DetectsVerificationMismatch()
    {
        // M4A の複数値のように、書いた内容と格納結果がずれる状況を模したライター。
        RecordingTagWriter writer = new() { JoinMultipleValues = true };
        RestoreService service = new(writer, new EchoTagReader(writer));

        RestoreResult result = await service.ApplyAsync(
            LIBRARY_ROOT,
            [new RestoreItem("01.m4a", TagField.AlbumArtist, [], ["Peter Pears(T)", "Hermann Prey(BR)"])]);

        Assert.Equal(1, result.SucceededFiles);

        VerificationMismatch mismatch = Assert.Single(result.Mismatches);
        Assert.Equal(TagField.AlbumArtist, mismatch.Field);
        Assert.Equal(["Peter Pears(T)", "Hermann Prey(BR)"], mismatch.Expected);
        Assert.Equal(["Peter Pears(T); Hermann Prey(BR)"], mismatch.Actual);
    }

    /// <summary>
    /// 1 ファイルにつき 1 回の書き込みにまとめることを確認する。
    /// </summary>
    [Fact]
    public async Task WritesOncePerFile()
    {
        RecordingTagWriter writer = new();
        RestoreService service = new(writer, new EchoTagReader(writer));

        await service.ApplyAsync(
            LIBRARY_ROOT,
            [
                new RestoreItem("01.m4a", TagField.Composer, [], ["Anton Bruckner"]),
                new RestoreItem("01.m4a", TagField.Conductor, [], ["Günter Wand"]),
                new RestoreItem("01.m4a", TagField.Genre, [], ["Classic"]),
            ]);

        Assert.Single(writer.Writes);
        Assert.Equal(3, writer.Writes.Single().Value.Count);
    }

    /// <summary>
    /// テスト用のスナップショットを組み立てる。
    /// </summary>
    private static TagSnapshot BuildSnapshot(IEnumerable<(string Path, TagField Field, string[] Values)> entries)
    {
        List<SnapshotTrack> tracks = [];

        foreach (var group in entries.GroupBy(entry => entry.Path, StringComparer.Ordinal))
        {
            Dictionary<string, string[]> fields = group
                .ToDictionary(entry => entry.Field.ToString(), entry => entry.Values);

            tracks.Add(new SnapshotTrack(group.Key, "M4a", fields, new Dictionary<string, string[]>()));
        }

        return new TagSnapshot(
            BackupConst.SCHEMA_VERSION,
            DateTimeOffset.Now,
            LIBRARY_ROOT,
            tracks.Count,
            tracks);
    }

    /// <summary>
    /// テスト用のスキャン結果を組み立てる。
    /// </summary>
    private static ScanResult BuildScan(IEnumerable<(string Path, TagField Field, string[] Values)> entries)
    {
        List<TrackTags> tracks = [];

        foreach (var group in entries.GroupBy(entry => entry.Path, StringComparer.Ordinal))
        {
            tracks.Add(new TrackTags
            {
                RelativePath = group.Key,
                FullPath = Path.Combine(LIBRARY_ROOT, group.Key),
                Format = AudioFormat.M4a,
                Fields = TrackTags.BuildFields(
                    group.Select(entry =>
                        new KeyValuePair<TagField, IReadOnlyList<string>>(entry.Field, entry.Values))),
                RawTags = new Dictionary<string, string[]>(),
            });
        }

        return new ScanResult(LIBRARY_ROOT, tracks, [], TimeSpan.Zero);
    }

    /// <summary>
    /// 書き込み内容を記録するだけのライター。実ファイルには触らない。
    /// </summary>
    private sealed class RecordingTagWriter : ITagWriter
    {
        /// <summary>相対パスごとの書き込み内容。</summary>
        public Dictionary<string, Dictionary<TagField, IReadOnlyList<string>>> Writes { get; } = [];

        /// <summary>この相対パスの書き込みを失敗させる。</summary>
        public string? FailingPath { get; init; }

        /// <summary>複数値を連結して格納する。M4A の制約を模す。</summary>
        public bool JoinMultipleValues { get; init; }

        /// <inheritdoc />
        public void Write(string fullPath, IReadOnlyDictionary<TagField, IReadOnlyList<string>> fields)
        {
            string relativePath = Path.GetRelativePath(LIBRARY_ROOT, fullPath);

            if (FailingPath is not null && relativePath == FailingPath)
            {
                throw new IOException("書き込みに失敗（テスト用）");
            }

            Dictionary<TagField, IReadOnlyList<string>> stored = [];

            foreach ((TagField field, IReadOnlyList<string> values) in fields)
            {
                stored[field] = JoinMultipleValues && values.Count > 1
                    ? [string.Join(TrackTags.VALUE_JOIN_SEPARATOR, values)]
                    : values;
            }

            Writes[relativePath] = stored;
        }
    }

    /// <summary>
    /// 直前に書き込まれた内容をそのまま返すリーダー。照合工程の検証に使う。
    /// </summary>
    private sealed class EchoTagReader(RecordingTagWriter writer) : ITagReader
    {
        /// <inheritdoc />
        public TrackTags Read(string fullPath, string relativePath)
        {
            Dictionary<TagField, IReadOnlyList<string>> stored = writer.Writes.TryGetValue(
                relativePath,
                out Dictionary<TagField, IReadOnlyList<string>>? values)
                ? values
                : [];

            return new TrackTags
            {
                RelativePath = relativePath,
                FullPath = fullPath,
                Format = AudioFormat.M4a,
                Fields = TrackTags.BuildFields(
                    stored.Select(pair =>
                        new KeyValuePair<TagField, IReadOnlyList<string>>(pair.Key, pair.Value))),
                RawTags = new Dictionary<string, string[]>(),
            };
        }
    }
}
