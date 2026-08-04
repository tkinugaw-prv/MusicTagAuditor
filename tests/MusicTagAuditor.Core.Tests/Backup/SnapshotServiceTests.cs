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

    /// <summary>テスト対象。</summary>
    private readonly SnapshotService _service = new();

    /// <summary>
    /// テスト用の一時フォルダを用意する。
    /// </summary>
    public SnapshotServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "musicTagger.tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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
    /// テスト用のスキャン結果を組み立てる。
    /// </summary>
    private ScanResult BuildScan()
    {
        TrackTags bruckner = new()
        {
            RelativePath = "ブルックナー/01.m4a",
            FullPath = Path.Combine(_root, "ブルックナー", "01.m4a"),
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
            FullPath = Path.Combine(_root, "バッハ", "02.flac"),
            Format = AudioFormat.Flac,
            Fields = TrackTags.BuildFields(
            [
                new(TagField.AlbumArtist, ["Peter Pears(T)", "Hermann Prey(BR)"]),
            ]),
            RawTags = new Dictionary<string, string[]>(),
        };

        return new ScanResult(
            _root,
            [bruckner, bach],
            [new ScanFailure("壊れたファイル.mp3", "InvalidDataException: 読めない")],
            TimeSpan.FromSeconds(1));
    }
}
