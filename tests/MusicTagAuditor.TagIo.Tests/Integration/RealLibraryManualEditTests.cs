using MusicTagAuditor.Core.Applying;
using MusicTagAuditor.Core.Backup;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Editing;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;
using Xunit.Abstractions;

namespace MusicTagAuditor.TagIo.Tests.Integration;

/// <summary>
/// 実ライブラリの複製に対して、手編集 → 適用 → 照合 → 復元 の一巡を通す。
///
/// **実ライブラリそのものには一切書き込まない。** 検証は必ず複製に対して行う。
///
/// 段階 6 の主な使い道はフォルダ単位の一括入力である（docs/library-baseline-2026-08-03.md の
/// R-402: 指揮者が不明な 124 ファイル / 25 フォルダ）。実データでその形を通すのが目的。
/// </summary>
public sealed class RealLibraryManualEditTests(ITestOutputHelper output) : IDisposable
{
    /// <summary>複製するフォルダ数。</summary>
    private const int FOLDER_COUNT = 3;

    /// <summary>複製先のルート。</summary>
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "musicTagger.tests",
        Guid.NewGuid().ToString("N"));

    /// <summary>タグ読み取り。</summary>
    private readonly TagReader _reader = new();

    /// <summary>タグ書き込み。</summary>
    private readonly TagWriter _writer = new();

    /// <summary>
    /// 複製先を削除する。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// フォルダ単位の一括入力を適用し、読み戻し照合が通ることを確認する。
    /// そのうえで適用前スナップショットから完全に巻き戻せることも確認する。
    /// </summary>
    [RealLibraryFact]
    public async Task AppliesBulkManualEditAndRestoresBack()
    {
        await CopySpecimensAsync();

        ScanResult before = await new LibraryScanner(_reader).ScanAsync(_root);

        Assert.NotEmpty(before.Tracks);

        ManualEditSet edits = new();

        // フォルダ単位で指揮者と演奏団体を入れる。R-402 の解消はこの形になる。
        foreach (var folder in before.Tracks.GroupBy(track => Path.GetDirectoryName(track.RelativePath) ?? string.Empty))
        {
            edits.SetMany(folder, TagField.Conductor, "Günter Wand");
            edits.SetMany(folder, TagField.AlbumArtist, "NDR Sinfonieorchester");
        }

        IReadOnlyList<TagChange> changes = edits.ToChanges();

        output.WriteLine($"対象 {before.Tracks.Count} 件 / 手編集 {changes.Count} 項目");

        Assert.NotEmpty(changes);
        Assert.All(changes, change => Assert.Equal(ManualEditConst.RULE_ID, change.RuleId));

        ApplyResult result = await new ApplyService(_writer, _reader, new SnapshotService())
            .ApplyAsync(before, changes, portableLibraryPath: TagWriter.GetPortableLibraryPath());

        output.WriteLine($"適用: {result.SucceededFiles} / {result.AttemptedFiles} ファイル、{result.AppliedChanges} 項目");

        foreach (VerificationMismatch mismatch in result.Mismatches)
        {
            output.WriteLine($"  不一致: {mismatch.Summary}");
        }

        Assert.Empty(result.Failures);
        Assert.Empty(result.Mismatches);
        Assert.True(result.IsClean);

        // 実ファイルに反映されていること。M4A の指揮者は ©con に入っていないと AIMP から見えない。
        ScanResult after = await new LibraryScanner(_reader).ScanAsync(_root);

        Assert.All(after.Tracks, track => Assert.Equal("Günter Wand", track.Conductor));
        Assert.All(after.Tracks, track => Assert.Equal("NDR Sinfonieorchester", track.AlbumArtist));

        // 適用前スナップショットから完全に巻き戻せること。
        TagSnapshot snapshot = new SnapshotService().Load(result.BackupDirectory);
        RestorePlan plan = RestoreService.BuildPlan(result.BackupDirectory, snapshot, after);

        RestoreResult restore = await new RestoreService(_writer, _reader).ApplyAsync(_root, plan.Items);

        Assert.Empty(restore.Failures);
        Assert.Empty(restore.Mismatches);

        ScanResult restored = await new LibraryScanner(_reader).ScanAsync(_root);
        AssertSameTags(before, restored);

        output.WriteLine("手編集前の状態に完全に戻った");
    }

    /// <summary>
    /// 手編集でタグを消せることを確認する。
    ///
    /// 空欄にするのは原則が認める操作である（docs/TAGGING_POLICY.md 7.4。
    /// 誤った値で埋めるより空欄のほうが後から対処できる）。
    /// </summary>
    [RealLibraryFact]
    public async Task ClearsTagByManualEdit()
    {
        await CopySpecimensAsync();

        ScanResult before = await new LibraryScanner(_reader).ScanAsync(_root);

        TrackTags[] targets = [.. before.Tracks.Where(track => !string.IsNullOrEmpty(track.Album)).Take(5)];

        Assert.NotEmpty(targets);

        ManualEditSet edits = new();

        foreach (TrackTags track in targets)
        {
            edits.Set(track, TagField.Album, string.Empty);
        }

        IReadOnlyList<TagChange> changes = edits.ToChanges();

        Assert.All(changes, change => Assert.True(change.ClearsValue));

        ApplyResult result = await new ApplyService(_writer, _reader, new SnapshotService())
            .ApplyAsync(before, changes);

        Assert.Empty(result.Failures);
        Assert.Empty(result.Mismatches);
        Assert.Equal(targets.Length, result.AppliedChanges);

        ScanResult after = await new LibraryScanner(_reader).ScanAsync(_root);

        Dictionary<string, TrackTags> byPath = after.Tracks
            .ToDictionary(track => track.RelativePath, StringComparer.OrdinalIgnoreCase);

        foreach (TrackTags track in targets)
        {
            Assert.Null(byPath[track.RelativePath].Album);
        }

        output.WriteLine($"{targets.Length} ファイルの album を消した");
    }

    /// <summary>
    /// 手編集の検査が、実データに対して意図した点を拾うことを確認する。
    /// </summary>
    [RealLibraryFact]
    public async Task WarnsOnQuestionableManualInput()
    {
        await CopySpecimensAsync();

        ScanResult scan = await new LibraryScanner(_reader).ScanAsync(_root);
        DictionaryIndex dictionary = new(DictionaryLoader.LoadDefault());

        TrackTags track = scan.Tracks[0];
        ManualEditSet edits = new();

        edits.Set(track, TagField.Genre, "Classical");
        edits.Set(track, TagField.Date, "1993-01-22T08:00:00Z");
        edits.Set(track, TagField.Conductor, "カラヤン");

        IReadOnlyList<ManualEditWarning> warnings =
            ManualEditValidator.Validate(edits.ToChanges(), scan.Tracks, dictionary);

        foreach (ManualEditWarning warning in warnings)
        {
            output.WriteLine($"  {warning.Summary}");
        }

        Assert.Equal(3, warnings.Select(warning => warning.Field).Distinct().Count());
    }

    /// <summary>
    /// 2 つのスキャン結果のタグが一致することを確認する。
    /// </summary>
    private static void AssertSameTags(ScanResult expected, ScanResult actual)
    {
        Dictionary<string, TrackTags> actualByPath = actual.Tracks
            .ToDictionary(track => track.RelativePath, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(expected.Tracks.Count, actual.Tracks.Count);

        foreach (TrackTags expectedTrack in expected.Tracks)
        {
            TrackTags actualTrack = actualByPath[expectedTrack.RelativePath];

            foreach (TagField field in Enum.GetValues<TagField>())
            {
                Assert.Equal(expectedTrack.GetValues(field), actualTrack.GetValues(field));
            }
        }
    }

    /// <summary>
    /// 実ライブラリからフォルダ単位で検体を複製する。
    /// フォーマットが偏らないよう、M4A・FLAC・ID3 のフォルダを 1 つずつ拾う。
    /// </summary>
    private async Task CopySpecimensAsync()
    {
        string libraryRoot = IntegrationConst.ResolveLibraryRoot();

        ScanResult source = await new LibraryScanner(_reader).ScanAsync(libraryRoot);

        var folders = source.Tracks
            .GroupBy(track => Path.GetDirectoryName(track.RelativePath) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .GroupBy(folder => folder.First().Format)
            .Select(byFormat => byFormat.First())
            .Take(FOLDER_COUNT);

        foreach (var folder in folders)
        {
            foreach (TrackTags track in folder)
            {
                string destinationPath = Path.Combine(_root, track.RelativePath);

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(track.FullPath, destinationPath, overwrite: true);
                File.SetAttributes(destinationPath, FileAttributes.Normal);
            }
        }
    }
}
