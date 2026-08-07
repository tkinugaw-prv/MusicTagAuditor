using MusicTagAuditor.Core.Abstractions;
using MusicTagAuditor.Core.Applying;
using MusicTagAuditor.Core.Backup;
using MusicTagAuditor.Core.Editing;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;

namespace MusicTagAuditor.Core.Tests.Applying;

/// <summary>
/// 適用のテスト。
///
/// docs/SPEC.md 9章の 3 点を重点的に確認する。
/// 適用直前のスナップショット / 読み戻し照合 / 1 件の失敗で止めないこと。
/// </summary>
public sealed class ApplyServiceTests : IDisposable
{
    /// <summary>テスト用ライブラリのルート。</summary>
    private readonly string _root;

    /// <summary>書き込みの記録。</summary>
    private readonly RecordingTagWriter _writer;

    /// <summary>照合用の読み取り。</summary>
    private readonly EchoTagReader _reader;

    /// <summary>テスト対象。</summary>
    private readonly ApplyService _service;

    /// <summary>
    /// テスト用の一時フォルダを用意する。
    /// </summary>
    public ApplyServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MusicTagAuditor.tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _writer = new RecordingTagWriter(_root);
        _reader = new EchoTagReader(_writer);
        _service = new ApplyService(_writer, _reader, new SnapshotService());
    }

    /// <summary>
    /// 一時フォルダを削除する。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// **書き込む前にスナップショットが取られる**ことを確認する（docs/SPEC.md 8.2）。
    /// 利用者が明示的にバックアップを取っていなくても自動で取る。
    /// </summary>
    [Fact]
    public async Task CreatesSnapshotBeforeWriting()
    {
        ApplyResult result = await _service.ApplyAsync(
            BuildScan(),
            [Change("01.m4a", TagField.Genre, [], ["Classic"], "R-102")]);

        Assert.True(Directory.Exists(result.BackupDirectory));
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory, BackupConst.SNAPSHOT_FILE_NAME)));

        // 取得理由が適用前であること。後から履歴を見て何のバックアップか分かる必要がある。
        Assert.Contains(nameof(SnapshotReason.BeforeApply), ReadManifest(result.BackupDirectory), StringComparison.Ordinal);
    }

    /// <summary>
    /// スナップショットの補足に、何を適用したかが残ることを確認する。
    /// </summary>
    [Fact]
    public async Task RecordsAppliedRulesInSnapshotNote()
    {
        ApplyResult result = await _service.ApplyAsync(
            BuildScan(),
            [
                Change("01.m4a", TagField.Genre, [], ["Classic"], "R-102"),
                Change("02.m4a", TagField.Genre, [], ["Classic"], "R-102"),
                Change("01.m4a", TagField.Composer, ["Btuckner"], ["Anton Bruckner"], "R-201"),
            ]);

        string manifest = ReadManifest(result.BackupDirectory);

        Assert.Contains("R-102 2件", manifest, StringComparison.Ordinal);
        Assert.Contains("R-201 1件", manifest, StringComparison.Ordinal);
    }

    /// <summary>
    /// チェックされた修正案だけを書き込むことを確認する。
    /// </summary>
    [Fact]
    public async Task AppliesOnlySelectedChanges()
    {
        TagChange selected = Change("01.m4a", TagField.Genre, [], ["Classic"], "R-102");
        TagChange unselected = Change("01.m4a", TagField.Composer, ["Btuckner"], ["Anton Bruckner"], "R-201");
        unselected.IsSelected = false;

        ApplyResult result = await _service.ApplyAsync(BuildScan(), [selected, unselected]);

        Assert.Equal(1, result.AppliedChanges);
        Assert.True(result.IsClean);

        Dictionary<TagField, IReadOnlyList<string>> written = _writer.Writes["01.m4a"];
        Assert.Equal(["Classic"], written[TagField.Genre]);
        Assert.DoesNotContain(TagField.Composer, written.Keys);
    }

    /// <summary>
    /// 修正値を持たない項目は、チェックされていても書き込まないことを確認する。
    /// 「確信が持てない項目は書き換えない」（docs/TAGGING_POLICY.md 7.4）。
    /// </summary>
    [Fact]
    public async Task SkipsChangesWithoutFixEvenWhenSelected()
    {
        TagChange noFix = Change("01.m4a", TagField.Artist, ["Richard Wagner"], [], "R-203");
        noFix.IsSelected = true;

        ApplyResult result = await _service.ApplyAsync(BuildScan(), [noFix]);

        Assert.Equal(0, result.AttemptedFiles);
        Assert.Empty(_writer.Writes);
    }

    /// <summary>
    /// 保留の項目を書き込まないことを確認する（<c>HOLD_ERA_UNKNOWN</c>）。
    /// </summary>
    [Fact]
    public async Task SkipsHeldChanges()
    {
        TagChange held = new(
            "01.m4a",
            TagField.AlbumArtist,
            ["Leningrad Philharmonic"],
            [],
            "R-209",
            "date が空欄のため保留",
            Severity.Error,
            HoldReason.EraUnknown)
        {
            IsSelected = true,
        };

        ApplyResult result = await _service.ApplyAsync(BuildScan(), [held]);

        Assert.Equal(0, result.AttemptedFiles);
        Assert.Empty(_writer.Writes);
    }

    /// <summary>
    /// 1 ファイルにつき 1 回の書き込みにまとめることを確認する。
    /// </summary>
    [Fact]
    public async Task WritesOncePerFile()
    {
        await _service.ApplyAsync(
            BuildScan(),
            [
                Change("01.m4a", TagField.Genre, [], ["Classic"], "R-102"),
                Change("01.m4a", TagField.Composer, ["Btuckner"], ["Anton Bruckner"], "R-201"),
                Change("01.m4a", TagField.DiscNumber, [], ["1/1"], "R-103"),
            ]);

        Assert.Single(_writer.Writes);
        Assert.Equal(3, _writer.Writes["01.m4a"].Count);
    }

    /// <summary>
    /// **書き込めたのに値が違う場合を検出する**ことを確認する（工程 6）。
    /// </summary>
    [Fact]
    public async Task DetectsVerificationMismatch()
    {
        _writer.JoinMultipleValues = true;

        ApplyResult result = await _service.ApplyAsync(
            BuildScan(),
            [Change("01.m4a", TagField.AlbumArtist, [], ["Peter Pears(T)", "Hermann Prey(BR)"], "R-206")]);

        Assert.Equal(1, result.SucceededFiles);
        Assert.False(result.IsClean);

        VerificationMismatch mismatch = Assert.Single(result.Mismatches);
        Assert.Equal(TagField.AlbumArtist, mismatch.Field);
        Assert.Equal(["Peter Pears(T); Hermann Prey(BR)"], mismatch.Actual);
    }

    /// <summary>
    /// 1 ファイルの失敗で全体を止めないことを確認する。
    /// </summary>
    [Fact]
    public async Task ContinuesAfterWriteFailure()
    {
        _writer.FailingPath = "壊れた.m4a";

        ApplyResult result = await _service.ApplyAsync(
            BuildScan(),
            [
                Change("01.m4a", TagField.Genre, [], ["Classic"], "R-102"),
                Change("壊れた.m4a", TagField.Genre, [], ["Classic"], "R-102"),
                Change("02.m4a", TagField.Genre, [], ["Classic"], "R-102"),
            ]);

        Assert.Equal(3, result.AttemptedFiles);
        Assert.Equal(2, result.SucceededFiles);
        Assert.Equal("壊れた.m4a", Assert.Single(result.Failures).RelativePath);
        Assert.False(result.IsClean);
    }

    /// <summary>
    /// **同じフィールドに異なる修正案が選ばれている場合は書き込まない**ことを確認する。
    /// 機械的にどちらかを採るのは危険であり、報告に留める。
    /// </summary>
    [Fact]
    public async Task DoesNotWriteConflictingProposals()
    {
        ApplyResult result = await _service.ApplyAsync(
            BuildScan(),
            [
                Change("01.m4a", TagField.AlbumArtist, ["Leningrad Philharmonic"], ["Leningrad Philharmonic Orchestra"], "R-209"),
                Change("01.m4a", TagField.AlbumArtist, ["Leningrad Philharmonic"], ["Saint Petersburg Philharmonic Orchestra"], "R-202"),
            ]);

        Assert.Empty(_writer.Writes);
        Assert.False(result.IsClean);

        ApplyConflict conflict = Assert.Single(result.Conflicts);
        Assert.Equal(TagField.AlbumArtist, conflict.Field);
        Assert.Equal(2, conflict.Proposals.Count);
    }

    /// <summary>
    /// 別々のルールが同じ結論に達した場合は競合にしないことを確認する。
    /// </summary>
    [Fact]
    public async Task TreatsIdenticalProposalsAsSingleChange()
    {
        ApplyResult result = await _service.ApplyAsync(
            BuildScan(),
            [
                Change("01.m4a", TagField.Genre, [], ["Classic"], "R-101"),
                Change("01.m4a", TagField.Genre, [], ["Classic"], "R-102"),
            ]);

        Assert.Empty(result.Conflicts);
        Assert.Equal(1, result.AppliedChanges);
        Assert.True(result.IsClean);
    }

    /// <summary>
    /// 競合した項目以外は書き込むことを確認する。1 つの競合で他を巻き添えにしない。
    /// </summary>
    [Fact]
    public async Task AppliesOtherFieldsWhenOneFieldConflicts()
    {
        ApplyResult result = await _service.ApplyAsync(
            BuildScan(),
            [
                Change("01.m4a", TagField.AlbumArtist, [], ["A"], "R-202"),
                Change("01.m4a", TagField.AlbumArtist, [], ["B"], "R-209"),
                Change("01.m4a", TagField.Genre, [], ["Classic"], "R-102"),
            ]);

        Assert.Single(result.Conflicts);
        Assert.Equal(["Classic"], _writer.Writes["01.m4a"][TagField.Genre]);
        Assert.DoesNotContain(TagField.AlbumArtist, _writer.Writes["01.m4a"].Keys);
    }

    /// <summary>
    /// **手編集はルールの修正案より優先される**ことを確認する（段階 6）。
    /// 人間が明示的に入れた値のほうが強い。
    /// </summary>
    [Fact]
    public async Task PrefersManualEditOverRuleProposal()
    {
        await _service.ApplyAsync(
            BuildScan(),
            [
                Change("01.m4a", TagField.Conductor, ["Wand"], ["Günter Wand"], "R-202"),
                Change("01.m4a", TagField.Conductor, ["Wand"], ["Herbert von Karajan"], ManualEditConst.RULE_ID),
            ]);

        Assert.Equal(["Herbert von Karajan"], _writer.Writes["01.m4a"][TagField.Conductor]);
    }

    /// <summary>
    /// 手編集で解消した競合も報告することを確認する。
    /// どの案を捨てたのかを利用者が知る必要がある。
    /// </summary>
    [Fact]
    public async Task StillReportsConflictResolvedByManualEdit()
    {
        ApplyResult result = await _service.ApplyAsync(
            BuildScan(),
            [
                Change("01.m4a", TagField.Conductor, ["Wand"], ["Günter Wand"], "R-202"),
                Change("01.m4a", TagField.Conductor, ["Wand"], ["Herbert von Karajan"], ManualEditConst.RULE_ID),
            ]);

        ApplyConflict conflict = Assert.Single(result.Conflicts);

        Assert.True(conflict.IsResolved);
        Assert.Equal("Herbert von Karajan", conflict.AdoptedValue);
        Assert.Contains("R-202", conflict.Summary, StringComparison.Ordinal);

        // 捨てた案があるので「問題なく完了」にはしない。
        Assert.False(result.IsClean);
    }

    /// <summary>
    /// 手編集で値を消す指示が書き込まれることを確認する。
    ///
    /// 修正案が空であること（ルールの「決められなかった」）と、削除の指示は別物である。
    /// 空欄にするのは原則が認める操作（docs/TAGGING_POLICY.md 7.4）。
    /// </summary>
    [Fact]
    public async Task AppliesManualClear()
    {
        TagChange clear = new(
            "01.m4a",
            TagField.AlbumArtist,
            ["Gustav Mahler"],
            [],
            ManualEditConst.RULE_ID,
            "手編集",
            Severity.Manual,
            HoldReason.None,
            ClearsValue: true);

        ApplyResult result = await _service.ApplyAsync(BuildScan(), [clear]);

        Assert.Equal(1, result.AppliedChanges);
        Assert.True(result.IsClean);
        Assert.Empty(_writer.Writes["01.m4a"][TagField.AlbumArtist]);
    }

    /// <summary>
    /// 進捗が全ファイル分通知されることを確認する。
    /// </summary>
    [Fact]
    public async Task ReportsProgressForEveryFile()
    {
        List<ApplyProgress> reports = [];

        await _service.ApplyAsync(
            BuildScan(),
            [
                Change("01.m4a", TagField.Genre, [], ["Classic"], "R-102"),
                Change("02.m4a", TagField.Genre, [], ["Classic"], "R-102"),
            ],
            progress: new SynchronousProgress<ApplyProgress>(reports.Add));

        Assert.Equal(2, reports.Count);
        Assert.All(reports, report => Assert.Equal(2, report.Total));
    }

    /// <summary>
    /// マニフェストの中身を読む。
    /// </summary>
    private static string ReadManifest(string backupDirectory)
    {
        return File.ReadAllText(Path.Combine(backupDirectory, BackupConst.MANIFEST_FILE_NAME));
    }

    /// <summary>
    /// テスト用の修正案を作る。
    /// </summary>
    private static TagChange Change(
        string relativePath,
        TagField field,
        string[] before,
        string[] after,
        string ruleId)
    {
        return new TagChange(relativePath, field, before, after, ruleId, "テスト用", Severity.Error);
    }

    /// <summary>
    /// テスト用のスキャン結果を作る。スナップショットの取得元になる。
    /// </summary>
    private ScanResult BuildScan()
    {
        TrackTags[] tracks =
        [
            .. new[] { "01.m4a", "02.m4a", "壊れた.m4a" }.Select(path => new TrackTags
            {
                RelativePath = path,
                FullPath = Path.Combine(_root, path),
                Format = AudioFormat.M4a,
                Fields = TrackTags.BuildFields([]),
                RawTags = new Dictionary<string, string[]>(),
            }),
        ];

        return new ScanResult(_root, tracks, [], TimeSpan.Zero);
    }

    /// <summary>
    /// 同期的に通知を受け取る進捗。<see cref="Progress{T}"/> は非同期に流すため順序も時刻も保証されない。
    /// </summary>
    private sealed class SynchronousProgress<T>(Action<T> onReport) : IProgress<T>
    {
        /// <inheritdoc />
        public void Report(T value)
        {
            onReport(value);
        }
    }

    /// <summary>
    /// 書き込み内容を記録するだけのライター。実ファイルには触らない。
    /// </summary>
    private sealed class RecordingTagWriter(string libraryRoot) : ITagWriter
    {
        /// <summary>相対パスごとの書き込み内容。</summary>
        public Dictionary<string, Dictionary<TagField, IReadOnlyList<string>>> Writes { get; } = [];

        /// <summary>この相対パスの書き込みを失敗させる。</summary>
        public string? FailingPath { get; set; }

        /// <summary>複数値を連結して格納する。M4A の制約を模す。</summary>
        public bool JoinMultipleValues { get; set; }

        /// <inheritdoc />
        public void Write(string fullPath, IReadOnlyDictionary<TagField, IReadOnlyList<string>> fields)
        {
            string relativePath = Path.GetRelativePath(libraryRoot, fullPath);

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
