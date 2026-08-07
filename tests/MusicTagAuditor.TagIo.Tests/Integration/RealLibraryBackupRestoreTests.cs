using MusicTagAuditor.Core.Backup;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;
using Xunit.Abstractions;

namespace MusicTagAuditor.TagIo.Tests.Integration;

/// <summary>
/// 実ライブラリのファイルを複製して、バックアップ → 破壊 → 復元 が成立するかを確認する。
///
/// **実ライブラリそのものには一切書き込まない。** 検証は必ず複製に対して行う。
/// 生成した検体では現れない実データ固有の状態（フリーフォーム atom、既存タグ、
/// 非 ASCII のパス）を通すことが目的。
/// </summary>
public sealed class RealLibraryBackupRestoreTests(ITestOutputHelper output) : IDisposable
{
    /// <summary>複製する検体の数。</summary>
    private const int SPECIMEN_COUNT = 40;

    /// <summary>複製先のルート。</summary>
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "MusicTagAuditor.tests",
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
    /// スナップショットを取り、タグを壊してから復元すると、元の状態に完全に戻ることを確認する。
    /// 書き込み機能を作る前に、復元手段が動くことを確認しておくのが段階 2 の目的。
    /// </summary>
    [RealLibraryFact]
    public async Task RestoresRealFilesToOriginalState()
    {
        IReadOnlyList<string> specimens = CopySpecimens();
        output.WriteLine($"複製した検体: {specimens.Count} 件");

        ScanResult original = await new LibraryScanner(_reader).ScanAsync(_root);
        Assert.Equal(specimens.Count, original.Tracks.Count);

        string backupDirectory = new SnapshotService().Create(
            original,
            SnapshotReason.Manual,
            portableLibraryPath: TagWriter.GetPortableLibraryPath());

        output.WriteLine($"バックアップ: {backupDirectory}");

        // タグを壊す。指揮者と作曲家を差し替え、ジャンルを消す。
        foreach (TrackTags track in original.Tracks)
        {
            _writer.Write(track.FullPath, new Dictionary<TagField, IReadOnlyList<string>>
            {
                [TagField.Conductor] = ["まちがった指揮者"],
                [TagField.Composer] = ["Btuckner"],
                [TagField.Genre] = [],
            });
        }

        ScanResult broken = await new LibraryScanner(_reader).ScanAsync(_root);

        TagSnapshot snapshot = new SnapshotService().Load(backupDirectory);
        RestorePlan plan = RestoreService.BuildPlan(backupDirectory, snapshot, broken);

        output.WriteLine($"復元計画: {plan.Items.Count} 項目");
        Assert.NotEmpty(plan.Items);
        Assert.Empty(plan.MissingFiles);

        // バックアップフォルダはスキャン対象外なので、増えたファイルとして現れてはならない。
        Assert.Empty(plan.AddedFiles);

        RestoreResult result = await new RestoreService(_writer, _reader).ApplyAsync(_root, plan.Items);

        output.WriteLine($"復元: {result.SucceededFiles} / {result.AttemptedFiles} ファイル、{result.RestoredItems} 項目");

        foreach (RestoreFailure failure in result.Failures)
        {
            output.WriteLine($"  失敗: {failure.RelativePath} — {failure.Message}");
        }

        foreach (VerificationMismatch mismatch in result.Mismatches)
        {
            output.WriteLine(
                $"  不一致: {mismatch.RelativePath} {mismatch.Field} "
                + $"expected=[{string.Join(" ⟂ ", mismatch.Expected)}] actual=[{string.Join(" ⟂ ", mismatch.Actual)}]");
        }

        Assert.Empty(result.Failures);
        Assert.Empty(result.Mismatches);

        // 復元後の状態が元と一致すること。
        ScanResult restored = await new LibraryScanner(_reader).ScanAsync(_root);
        AssertSameTags(original, restored);
    }

    /// <summary>
    /// 編集対象でない生タグ（<c>iTunNORM</c> 等）が、書き込みを経ても失われないことを確認する。
    /// </summary>
    [RealLibraryFact]
    public async Task PreservesUnrelatedRawTagsThroughWrite()
    {
        CopySpecimens();

        ScanResult original = await new LibraryScanner(_reader).ScanAsync(_root);

        TrackTags? withFreeform = original.Tracks
            .FirstOrDefault(track => track.RawTags.Keys.Any(key => key.StartsWith(TagIoConst.ATOM_FREEFORM, StringComparison.Ordinal)));

        Assert.True(withFreeform is not null, "フリーフォーム atom を持つ検体が複製されなかった");

        string[] freeformKeys = [.. withFreeform!.RawTags.Keys
            .Where(key => key.StartsWith(TagIoConst.ATOM_FREEFORM, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

        output.WriteLine($"対象: {withFreeform.RelativePath}");
        output.WriteLine($"フリーフォーム atom: {string.Join(", ", freeformKeys)}");

        _writer.Write(withFreeform.FullPath, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Composer] = ["Anton Bruckner"],
        });

        TrackTags after = _reader.Read(withFreeform.FullPath, withFreeform.RelativePath);

        foreach (string key in freeformKeys)
        {
            Assert.True(after.RawTags.ContainsKey(key), $"{key} が消失した");
            Assert.Equal(withFreeform.RawTags[key], after.RawTags[key]);
        }
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
    /// 実ライブラリからフォーマットが偏らないように検体を複製する。
    /// </summary>
    private IReadOnlyList<string> CopySpecimens()
    {
        string libraryRoot = IntegrationConst.RequireLibraryRoot();
        LibraryScanner scanner = new(_reader);

        // フォーマットごとに均等に取る。M4A だけだと ID3 の経路を通らない。
        var byExtension = scanner
            .EnumerateTargetFiles(libraryRoot)
            .GroupBy(path => Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        int perExtension = Math.Max(SPECIMEN_COUNT / Math.Max(byExtension.Count, 1), 1);

        List<string> copied = [];

        foreach (var group in byExtension)
        {
            foreach (string sourcePath in group.Take(perExtension))
            {
                string relativePath = Path.GetRelativePath(libraryRoot, sourcePath);
                string destinationPath = Path.Combine(_root, relativePath);

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, overwrite: true);
                File.SetAttributes(destinationPath, FileAttributes.Normal);

                copied.Add(destinationPath);
            }
        }

        return copied;
    }
}
