using System.Text.Json;
using MusicTagAuditor.Core.Backup;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;

namespace MusicTagAuditor.Core.Tests.Backup;

/// <summary>
/// スナップショットの取得・読み込みのテスト。
/// **アプリ無しでも復元できる形式であること**が要件なので、JSON の中身も直接検証する。
/// </summary>
public sealed class SnapshotServiceTests : IDisposable
{
    /// <summary>テスト用ライブラリのルート。</summary>
    private readonly string _root;

    /// <summary>ライブラリの外に置くバックアップ保存先。</summary>
    private readonly string _customBackupRoot;

    /// <summary>テスト対象。保存先を設定していない状態。</summary>
    private readonly SnapshotService _service = new();

    /// <summary>
    /// テスト用の一時フォルダを用意する。
    /// </summary>
    public SnapshotServiceTests()
    {
        string baseDirectory = Path.Combine(Path.GetTempPath(), "MusicTagAuditor.tests", Guid.NewGuid().ToString("N"));

        _root = Path.Combine(baseDirectory, "library");
        _customBackupRoot = Path.Combine(baseDirectory, "backups");

        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_customBackupRoot);
    }

    /// <summary>
    /// 一時フォルダを削除する。
    /// </summary>
    public void Dispose()
    {
        string baseDirectory = Path.GetDirectoryName(_root)!;

        if (Directory.Exists(baseDirectory))
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    /// <summary>
    /// スナップショット・マニフェスト・復元スクリプトが揃って書き出されることを確認する。
    /// </summary>
    [Fact]
    public void WritesSnapshotManifestAndRestoreScript()
    {
        string directory = _service.Create(BuildScan(), SnapshotReason.Manual);

        Assert.True(File.Exists(Path.Combine(directory, BackupConst.SNAPSHOT_FILE_NAME)));
        Assert.True(File.Exists(Path.Combine(directory, BackupConst.MANIFEST_FILE_NAME)));
        Assert.True(
            File.Exists(Path.Combine(directory, BackupConst.RESTORE_SCRIPT_FILE_NAME)),
            "アプリ無しで復元できることが要件なので、復元スクリプトの同梱は必須");
    }

    /// <summary>
    /// バックアップフォルダ名が <c>backup_yyyyMMddHHmmss</c> になることを確認する。
    /// スキャンの除外規則（backup_*）と一致していないと、バックアップを二重に読み込んでしまう。
    /// </summary>
    [Fact]
    public void UsesBackupPrefixedDirectoryName()
    {
        DateTimeOffset timestamp = new(2026, 8, 3, 3, 15, 0, TimeSpan.FromHours(9));

        string directory = _service.Create(BuildScan(), SnapshotReason.Manual, timestamp: timestamp);

        Assert.Equal("backup_20260803031500", Path.GetFileName(directory));
        Assert.StartsWith(
            ScanOptions.EXCLUDED_DIRECTORY_PREFIX,
            Path.GetFileName(directory),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// 書き出したスナップショットを読み戻せることを確認する。
    /// </summary>
    [Fact]
    public void RoundTripsSnapshot()
    {
        string directory = _service.Create(BuildScan(), SnapshotReason.BeforeApply, note: "R-201 の適用前");

        TagSnapshot snapshot = _service.Load(directory);

        Assert.Equal(BackupConst.SCHEMA_VERSION, snapshot.Version);
        Assert.Equal(_root, snapshot.LibraryRoot);
        Assert.Equal(2, snapshot.TrackCount);

        SnapshotTrack track = snapshot.Tracks.Single(t => t.Path == "ブルックナー/01.m4a");
        Assert.Equal("M4a", track.Format);
        Assert.Equal(["Anton Bruckner"], track.Fields[nameof(TagField.Composer)]);
        Assert.Equal(["Günter Wand"], track.Fields[nameof(TagField.Conductor)]);
    }

    /// <summary>
    /// 分割済みの複数値がそのまま記録されることを確認する。
    /// 1 値へ丸めると、壊れた状態を壊れたまま戻せなくなる。
    /// </summary>
    [Fact]
    public void KeepsMultipleValuesSeparate()
    {
        string directory = _service.Create(BuildScan(), SnapshotReason.Manual);

        SnapshotTrack track = _service.Load(directory).Tracks.Single(t => t.Path == "バッハ/02.flac");

        Assert.Equal(["Peter Pears(T)", "Hermann Prey(BR)"], track.Fields[nameof(TagField.AlbumArtist)]);
    }

    /// <summary>
    /// 編集対象でない生タグも記録されることを確認する（docs/SPEC.md 8.2「全ファイルの全タグ」）。
    /// </summary>
    [Fact]
    public void RecordsRawTags()
    {
        string directory = _service.Create(BuildScan(), SnapshotReason.Manual);

        SnapshotTrack track = _service.Load(directory).Tracks.Single(t => t.Path == "ブルックナー/01.m4a");

        Assert.Equal([" 000001F4 00000216"], track.RawTags["----:com.apple.iTunes:iTunNORM"]);
    }

    /// <summary>
    /// 日本語がエスケープされずに出力されることを確認する。
    /// 外部スクリプトと人が直接読むファイルなので、可読性は要件の一部。
    /// </summary>
    [Fact]
    public void WritesJapaneseWithoutEscaping()
    {
        string directory = _service.Create(BuildScan(), SnapshotReason.Manual);

        string json = File.ReadAllText(Path.Combine(directory, BackupConst.SNAPSHOT_FILE_NAME));

        Assert.Contains("ブルックナー", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u30D6", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// スナップショットを含まない <c>backup_*</c> フォルダを一覧に出さないことを確認する。
    /// 実ライブラリには音声本体を複製しただけの古い backup フォルダが存在する。
    /// </summary>
    [Fact]
    public void ListsOnlyDirectoriesContainingSnapshot()
    {
        Directory.CreateDirectory(Path.Combine(_root, "backup_20260802144324"));
        File.WriteAllBytes(Path.Combine(_root, "backup_20260802144324", "TAGTEST.m4a"), []);

        string valid = _service.Create(BuildScan(), SnapshotReason.Manual);

        BackupEntry entry = Assert.Single(_service.List(_root));

        Assert.Equal(valid, entry.DirectoryPath);
        Assert.NotNull(entry.Manifest);
        Assert.Equal(nameof(SnapshotReason.Manual), entry.Manifest.Reason);
    }

    /// <summary>
    /// マニフェストに取得理由と件数が記録されることを確認する。
    /// </summary>
    [Fact]
    public void RecordsReasonAndCountsInManifest()
    {
        string directory = _service.Create(BuildScan(), SnapshotReason.BeforeRestore, note: "復元前");

        using FileStream stream = File.OpenRead(Path.Combine(directory, BackupConst.MANIFEST_FILE_NAME));
        BackupManifest? manifest = JsonSerializer.Deserialize(stream, BackupJsonContext.Default.BackupManifest);

        Assert.NotNull(manifest);
        Assert.Equal(nameof(SnapshotReason.BeforeRestore), manifest.Reason);
        Assert.Equal(2, manifest.TrackCount);
        Assert.Equal(1, manifest.ReadFailureCount);
        Assert.Equal("復元前", manifest.Note);
    }

    /// <summary>
    /// フォルダ名から取得日時を読み取れることを確認する。
    /// </summary>
    [Fact]
    public void ParsesTimestampFromDirectoryName()
    {
        DateTimeOffset? parsed = SnapshotService.ParseTimestamp("backup_20260803031500");

        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(2026, 8, 3, 3, 15, 0), parsed.Value.DateTime);
        Assert.Null(SnapshotService.ParseTimestamp("なにか別のフォルダ"));
    }

    /// <summary>
    /// 連番付きのフォルダ名からも取得日時を読み取れることを確認する。
    /// 読めないと、共有の保存先で衝突した 2 件目だけ日時が空欄になる。
    /// </summary>
    [Fact]
    public void ParsesTimestampFromSequencedDirectoryName()
    {
        DateTimeOffset? parsed = SnapshotService.ParseTimestamp("backup_20260803031500_2");

        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(2026, 8, 3, 3, 15, 0), parsed.Value.DateTime);
    }

    /// <summary>
    /// 保存先を設定していなければ、従来どおりライブラリ直下に置くことを確認する。
    /// </summary>
    [Fact]
    public void WritesUnderLibraryRootWhenBackupRootNotConfigured()
    {
        string directory = _service.Create(BuildScan(), SnapshotReason.Manual);

        Assert.Equal(_root, Path.GetDirectoryName(directory));
    }

    /// <summary>
    /// 保存先を設定したら、ライブラリの外でもそこに置くことを確認する。
    /// </summary>
    [Fact]
    public void WritesUnderConfiguredBackupRoot()
    {
        string directory = CreateServiceWithCustomRoot().Create(BuildScan(), SnapshotReason.Manual);

        Assert.Equal(_customBackupRoot, Path.GetDirectoryName(directory));
        Assert.True(File.Exists(Path.Combine(directory, BackupConst.SNAPSHOT_FILE_NAME)));
    }

    /// <summary>
    /// 保存先を空欄に戻したら、ライブラリ直下に戻ることを確認する。
    /// 設定は毎回読み直す（値を握り込まない）ことの確認でもある。
    /// </summary>
    [Fact]
    public void FallsBackToLibraryRootWhenConfiguredRootIsBlank()
    {
        SnapshotService service = new(() => "   ");

        string directory = service.Create(BuildScan(), SnapshotReason.Manual);

        Assert.Equal(_root, Path.GetDirectoryName(directory));
    }

    /// <summary>
    /// 同じ秒に取得しても上書きされないことを確認する。
    /// 保存先を複数ライブラリで共有すると衝突しうる（ライブラリ直下では起こらなかった）。
    /// </summary>
    [Fact]
    public void AppendsSequenceWhenDirectoryNameCollides()
    {
        DateTimeOffset timestamp = new(2026, 8, 3, 3, 15, 0, TimeSpan.FromHours(9));
        SnapshotService service = CreateServiceWithCustomRoot();

        string first = service.Create(BuildScan(), SnapshotReason.Manual, timestamp: timestamp);
        string second = service.Create(
            BuildScan(Path.Combine(_root, "別ライブラリ")),
            SnapshotReason.Manual,
            timestamp: timestamp);

        Assert.Equal("backup_20260803031500", Path.GetFileName(first));
        Assert.Equal("backup_20260803031500_2", Path.GetFileName(second));
        Assert.True(File.Exists(Path.Combine(first, BackupConst.SNAPSHOT_FILE_NAME)));
        Assert.True(File.Exists(Path.Combine(second, BackupConst.SNAPSHOT_FILE_NAME)));
    }

    /// <summary>
    /// 一覧が設定した保存先とライブラリ直下の両方を拾い、他ライブラリのものは混ぜないことを確認する。
    /// 保存先を変えたあとも過去の履歴が消えないことが要件。
    /// </summary>
    [Fact]
    public void ListsBackupsFromBothRootsExcludingOtherLibraries()
    {
        // 設定前にライブラリ直下へ取ったぶん。
        string legacy = _service.Create(
            BuildScan(),
            SnapshotReason.Manual,
            timestamp: new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.FromHours(9)));

        SnapshotService service = CreateServiceWithCustomRoot();

        string current = service.Create(
            BuildScan(),
            SnapshotReason.Manual,
            timestamp: new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.FromHours(9)));

        // 保存先を共有している別ライブラリのぶん。一覧に出してはいけない。
        service.Create(
            BuildScan(Path.Combine(_root, "別ライブラリ")),
            SnapshotReason.Manual,
            timestamp: new DateTimeOffset(2026, 8, 4, 10, 0, 0, TimeSpan.FromHours(9)));

        IReadOnlyList<BackupEntry> entries = service.List(_root);

        Assert.Equal(2, entries.Count);
        Assert.Equal(current, entries[0].DirectoryPath);
        Assert.Equal(legacy, entries[1].DirectoryPath);
    }

    /// <summary>
    /// 保存先がライブラリ直下と同じでも二重に数えないことを確認する。
    /// </summary>
    [Fact]
    public void DoesNotDuplicateWhenBackupRootIsLibraryRoot()
    {
        SnapshotService service = new(() => _root);

        string directory = service.Create(BuildScan(), SnapshotReason.Manual);

        BackupEntry entry = Assert.Single(service.List(_root));
        Assert.Equal(directory, entry.DirectoryPath);
    }

    /// <summary>
    /// 設定した保存先を使うテスト対象を作る。
    /// </summary>
    private SnapshotService CreateServiceWithCustomRoot()
    {
        return new SnapshotService(() => _customBackupRoot);
    }

    /// <summary>
    /// テスト用のスキャン結果を組み立てる。
    /// </summary>
    /// <param name="libraryRoot">ライブラリのルート。省略時はテスト用ライブラリ。</param>
    private ScanResult BuildScan(string? libraryRoot = null)
    {
        string root = libraryRoot ?? _root;
        TrackTags bruckner = new()
        {
            RelativePath = "ブルックナー/01.m4a",
            FullPath = Path.Combine(root, "ブルックナー", "01.m4a"),
            Format = AudioFormat.M4a,
            Fields = TrackTags.BuildFields(
            [
                new(TagField.Composer, ["Anton Bruckner"]),
                new(TagField.Conductor, ["Günter Wand"]),
                new(TagField.Genre, ["Classic"]),
            ]),
            RawTags = new Dictionary<string, string[]>
            {
                ["----:com.apple.iTunes:iTunNORM"] = [" 000001F4 00000216"],
                ["©wrt"] = ["Anton Bruckner"],
            },
        };

        TrackTags bach = new()
        {
            RelativePath = "バッハ/02.flac",
            FullPath = Path.Combine(root, "バッハ", "02.flac"),
            Format = AudioFormat.Flac,
            Fields = TrackTags.BuildFields(
            [
                new(TagField.AlbumArtist, ["Peter Pears(T)", "Hermann Prey(BR)"]),
            ]),
            RawTags = new Dictionary<string, string[]>(),
        };

        return new ScanResult(
            root,
            [bruckner, bach],
            [new ScanFailure("壊れたファイル.mp3", "InvalidDataException: 読めない")],
            TimeSpan.FromSeconds(1));
    }
}
