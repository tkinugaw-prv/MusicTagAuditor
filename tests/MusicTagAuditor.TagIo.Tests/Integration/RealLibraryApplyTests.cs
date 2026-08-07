using MusicTagAuditor.Core.Applying;
using MusicTagAuditor.Core.Backup;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Inspection.Rules;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;
using Xunit.Abstractions;

namespace MusicTagAuditor.TagIo.Tests.Integration;

/// <summary>
/// 実ライブラリの複製に対して、検査 → 適用 → 照合 → 復元 の一巡を通す。
///
/// **実ライブラリそのものには一切書き込まない。** 検証は必ず複製に対して行う。
/// 生成した検体では現れない実データ固有の状態を通すことが目的。
/// </summary>
public sealed class RealLibraryApplyTests(ITestOutputHelper output) : IDisposable
{
    /// <summary>複製する検体の目安（フォーマット 3 種 × 下記の合計）。</summary>
    private const int SPECIMEN_COUNT = 200;

    /// <summary>フォーマットごとに拾う検体数。フォーマットの偏りを避ける。</summary>
    private const int SPECIMEN_COUNT_PER_FORMAT = SPECIMEN_COUNT / 3;

    /// <summary>
    /// 複製にだけ書き込む違反値。<see cref="GenreNotClassicRule.CANONICAL_GENRE"/> と異なるので
    /// R-101 が確実に検出する。
    /// </summary>
    private const string SEEDED_GENRE = "Classical";

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
    /// 検査で選ばれた修正案を適用し、読み戻し照合が通ることを確認する。
    /// そのうえで適用前スナップショットから完全に巻き戻せることも確認する。
    /// </summary>
    [RealLibraryFact]
    public async Task AppliesInspectionResultAndRestoresBack()
    {
        await CopySpecimensAsync();

        ScanResult before = await new LibraryScanner(_reader).ScanAsync(_root);
        DictionaryIndex dictionary = new(DictionaryLoader.LoadDefault());

        InspectionResult inspection = new InspectionEngine().Inspect(new InspectionContext(before, dictionary));

        TagChange[] selected = [.. inspection.AllChanges.Where(change => change.IsSelected && change.HasFix)];

        output.WriteLine($"対象 {before.Tracks.Count} 件 / 検出 {inspection.TotalChanges} 件 / 適用 {selected.Length} 件");

        foreach (var group in selected.GroupBy(change => change.RuleId).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"  {group.Key}: {group.Count()} 件");
        }

        Assert.NotEmpty(selected);

        ApplyService service = new(_writer, _reader, new SnapshotService());

        ApplyResult result = await service.ApplyAsync(
            before,
            selected,
            portableLibraryPath: TagWriter.GetPortableLibraryPath());

        output.WriteLine("");
        output.WriteLine(
            $"適用: {result.SucceededFiles} / {result.AttemptedFiles} ファイル、{result.AppliedChanges} 項目");

        foreach (ApplyFailure failure in result.Failures)
        {
            output.WriteLine($"  失敗: {failure.RelativePath} — {failure.Message}");
        }

        foreach (VerificationMismatch mismatch in result.Mismatches)
        {
            output.WriteLine($"  不一致: {mismatch.Summary}");
        }

        foreach (ApplyConflict conflict in result.Conflicts)
        {
            output.WriteLine($"  競合: {conflict.Summary}");
        }

        Assert.Empty(result.Failures);
        Assert.Empty(result.Mismatches);
        Assert.Empty(result.Conflicts);
        Assert.True(result.IsClean);

        // 適用後は同じ修正案が再検出されないこと。直したのに直っていない状態を弾く。
        ScanResult after = await new LibraryScanner(_reader).ScanAsync(_root);
        InspectionResult reinspection = new InspectionEngine().Inspect(new InspectionContext(after, dictionary));

        int remaining = reinspection.AllChanges.Count(change => change.IsSelected && change.HasFix);
        output.WriteLine($"再検査で残った適用対象: {remaining} 件");

        Assert.Equal(0, remaining);

        // 適用前スナップショットから完全に巻き戻せること。
        TagSnapshot snapshot = new SnapshotService().Load(result.BackupDirectory);
        RestorePlan plan = RestoreService.BuildPlan(result.BackupDirectory, snapshot, after);

        output.WriteLine($"復元計画: {plan.Items.Count} 項目");
        Assert.NotEmpty(plan.Items);

        RestoreResult restore = await new RestoreService(_writer, _reader).ApplyAsync(_root, plan.Items);

        Assert.Empty(restore.Failures);
        Assert.Empty(restore.Mismatches);

        ScanResult restored = await new LibraryScanner(_reader).ScanAsync(_root);
        AssertSameTags(before, restored);

        output.WriteLine("適用前の状態に完全に戻った");
    }

    /// <summary>
    /// 適用直前のスナップショットが、バックアップ履歴に「適用前」として残ることを確認する。
    /// </summary>
    [RealLibraryFact]
    public async Task RecordsSnapshotAsBeforeApply()
    {
        await CopySpecimensAsync();

        ScanResult before = await new LibraryScanner(_reader).ScanAsync(_root);
        DictionaryIndex dictionary = new(DictionaryLoader.LoadDefault());
        InspectionResult inspection = new InspectionEngine().Inspect(new InspectionContext(before, dictionary));

        ApplyService service = new(_writer, _reader, new SnapshotService());

        ApplyResult result = await service.ApplyAsync(
            before,
            inspection.AllChanges.Where(change => change.IsSelected && change.HasFix));

        BackupEntry entry = Assert.Single(new SnapshotService().List(_root));

        Assert.Equal(result.BackupDirectory, entry.DirectoryPath);
        Assert.NotNull(entry.Manifest);
        Assert.Equal(nameof(SnapshotReason.BeforeApply), entry.Manifest.Reason);

        output.WriteLine($"バックアップ: {entry.DirectoryName}");
        output.WriteLine($"補足: {entry.Manifest.Note}");

        Assert.NotNull(entry.Manifest.Note);
        Assert.Contains("R-", entry.Manifest.Note, StringComparison.Ordinal);
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
    /// 実ライブラリから検体を複製し、複製にだけ違反を書き込む。
    ///
    /// 実ライブラリはアプリで修正を適用済みのことがあり、自動修正できる違反が 1 件も
    /// 残っていない場合がある。修正案の有無を実データの汚れ具合に委ねると、ライブラリを
    /// きれいにするほどこのテストが落ちてしまう。そのため、フォーマットごとにフォルダを
    /// 拾って複製したうえで（<see cref="RealLibraryManualEditTests"/> と同じ選び方）、
    /// 複製にだけ genre を意図的に壊して R-101 の修正案を自前で用意する。実ファイル固有の
    /// 癖（フォーマット別の書き込み経路など）は複製元のまま残るので、生成した検体では
    /// 確認できない部分は引き続き通せる。
    ///
    /// フォルダ単位で複製するのは、指揮者の特定が同一フォルダの他ファイルを見るため
    /// （docs/TAGGING_POLICY.md 6.2）。
    /// </summary>
    private async Task CopySpecimensAsync()
    {
        string libraryRoot = IntegrationConst.RequireLibraryRoot();

        ScanResult source = await new LibraryScanner(_reader).ScanAsync(libraryRoot);

        var foldersByFormat = source.Tracks
            .GroupBy(track => Path.GetDirectoryName(track.RelativePath) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .GroupBy(folder => folder.First().Format);

        IEnumerable<TrackTags> specimens = foldersByFormat
            .SelectMany(byFormat => byFormat
                .SelectMany(folder => folder)
                .Take(SPECIMEN_COUNT_PER_FORMAT));

        foreach (TrackTags track in specimens)
        {
            string destinationPath = Path.Combine(_root, track.RelativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(track.FullPath, destinationPath, overwrite: true);
            File.SetAttributes(destinationPath, FileAttributes.Normal);

            _writer.Write(destinationPath, new Dictionary<TagField, IReadOnlyList<string>>
            {
                [TagField.Genre] = [SEEDED_GENRE],
            });
        }
    }
}
