using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;
using Xunit.Abstractions;

namespace MusicTagAuditor.TagIo.Tests.Integration;

/// <summary>
/// 実ライブラリに対して検査を回し、docs/library-baseline-2026-08-03.md と突き合わせる。
///
/// **これは答え合わせのためのテストである。** 乖離があれば、ルール実装かデータ理解の
/// どちらかがずれている。実ライブラリには書き込まない（読み取りのみ）。
/// </summary>
public sealed class RealLibraryInspectionTests(ITestOutputHelper output)
{
    /// <summary>
    /// 検査結果をルール別に出力し、件数がベースラインと整合するかを確認する。
    /// </summary>
    [RealLibraryFact]
    public async Task InspectsRealLibrary()
    {
        string root = IntegrationConst.RequireLibraryRoot();

        ScanResult scan = await new LibraryScanner(new TagReader()).ScanAsync(root);
        DictionaryIndex dictionary = IntegrationConst.LoadUserDictionary();
        InspectionContext context = new(scan, dictionary);

        InspectionResult result = new InspectionEngine().Inspect(context);

        int selected = result.AllChanges.Count(change => change.IsSelected);
        int holds = result.AllChanges.Count(change => change.HoldReason != HoldReason.None);

        output.WriteLine(
            $"対象 {scan.Tracks.Count} 件 / 検出 {result.TotalChanges} 件 / 既定で選択 {selected} 件"
            + $" / 保留 {holds} 件 / 所要 {result.Elapsed.TotalSeconds:F2} 秒");
        output.WriteLine("");
        output.WriteLine("| ID | 内容 | 検出 | 修正可 | 保留 |");
        output.WriteLine("|---|---|---|---|---|");

        foreach (RuleResult rule in result.Results)
        {
            output.WriteLine($"| {rule.RuleId} | {rule.Description} | {rule.Changes.Count} | {rule.FixableCount} | {rule.HoldCount} |");
        }

        // 修正案の実例をルールごとに数件ずつ出す。根拠が読めるかの確認も兼ねる。
        foreach (RuleResult rule in result.Results.Where(r => r.Changes.Count > 0))
        {
            output.WriteLine("");
            output.WriteLine($"### {rule.RuleId} {rule.Description}");

            foreach (var group in rule.Changes
                         .GroupBy(change => (change.Field, change.BeforeText, change.AfterText, change.Rationale))
                         .OrderByDescending(g => g.Count())
                         .Take(8))
            {
                output.WriteLine(
                    $"  x{group.Count()} [{group.Key.Field}] 「{group.Key.BeforeText}」 → 「{group.Key.AfterText}」"
                    + $"（{group.Key.Rationale}）");
            }
        }

        Assert.NotEmpty(result.Results);
    }

    /// <summary>
    /// 利用者辞書が検証を通ることを確認する。
    ///
    /// **作品エントリは手で書くため、ここで止まらないと気づけない。** 自然キー（作曲家 + 作品名）や
    /// エイリアスが重複しても索引は先勝ちで、後から書いたほうは黙って捨てられる（docs/SPEC.md 7.4.1）。
    /// </summary>
    [RealLibraryFact]
    public void ValidatesUserDictionary()
    {
        TagDictionary dictionary = IntegrationConst.LoadUserDictionary().Dictionary;

        IReadOnlyList<DictionaryIssue> issues = DictionaryValidator.Validate(dictionary);

        output.WriteLine(
            $"作品 {dictionary.Works.Count} 件 / 個別例外 {dictionary.AlbumOverrides.Count} 件"
            + $" / 指摘 {issues.Count} 件");

        foreach (DictionaryIssue issue in issues)
        {
            output.WriteLine($"  {issue.Summary}");
        }

        Assert.False(DictionaryValidator.HasError(issues));
    }

    /// <summary>
    /// 保護対象（docs/TAGGING_POLICY.md 2.3）が検査から除外されていることを確認する。
    /// 除外できていないと R-207 / R-208 が誤検出だらけになる。
    /// </summary>
    [RealLibraryFact]
    public async Task ExcludesProtectedAlbumArtistsFromInspection()
    {
        string root = IntegrationConst.RequireLibraryRoot();

        ScanResult scan = await new LibraryScanner(new TagReader()).ScanAsync(root);
        DictionaryIndex dictionary = IntegrationConst.LoadUserDictionary();

        string[] protectedPaths = [.. scan.Tracks
            .Where(track => track.GetValues(TagField.AlbumArtist).Any(dictionary.IsProtectedAlbumArtist))
            .Select(track => track.RelativePath)];

        output.WriteLine($"保護対象のファイル: {protectedPaths.Length} 件");

        Assert.Equal(33, protectedPaths.Length);

        InspectionResult result = new InspectionEngine().Inspect(new InspectionContext(scan, dictionary));

        TagChange[] onProtected = [.. result.AllChanges
            .Where(change => change.Field == TagField.AlbumArtist && protectedPaths.Contains(change.RelativePath))];

        foreach (TagChange change in onProtected)
        {
            output.WriteLine($"  {change.RuleId} {change.RelativePath} 「{change.BeforeText}」");
        }

        Assert.Empty(onProtected);
    }
}
