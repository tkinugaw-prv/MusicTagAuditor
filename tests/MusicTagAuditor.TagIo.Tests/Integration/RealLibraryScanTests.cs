using System.Globalization;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;
using Xunit.Abstractions;

namespace MusicTagAuditor.TagIo.Tests.Integration;

/// <summary>
/// 実ライブラリに対するスキャンの結合テスト。
/// 非機能要件（1,000 ファイルを 10 秒以内。docs/SPEC.md 11章）の確認を兼ねる。
/// 実ライブラリが無い環境（CI 等）では自動的にスキップされる。
/// </summary>
public sealed class RealLibraryScanTests(ITestOutputHelper output)
{
    /// <summary>非機能要件: 1,000 ファイルあたりの許容秒数。</summary>
    private const double SECONDS_PER_1000_FILES = 10.0;

    /// <summary>
    /// 実ライブラリを走査し、性能要件を満たすことと除外規則が効いていることを確認する。
    /// </summary>
    [RealLibraryFact]
    public async Task ScansRealLibraryWithinPerformanceBudget()
    {
        string root = IntegrationConst.ResolveLibraryRoot();

        ScanResult result = await new LibraryScanner(new TagReader()).ScanAsync(root);

        output.WriteLine($"ルート: {root}");
        output.WriteLine($"読み取り: {result.Tracks.Count} 件 / 失敗: {result.Failures.Count} 件");
        output.WriteLine($"所要: {result.Elapsed.TotalSeconds:F2} 秒");

        foreach (IGrouping<AudioFormat, TrackTags> group in result.Tracks.GroupBy(track => track.Format))
        {
            output.WriteLine($"  {group.Key}: {group.Count()} 件");
        }

        foreach (ScanFailure failure in result.Failures)
        {
            output.WriteLine($"  失敗: {failure.RelativePath} — {failure.Message}");
        }

        Assert.NotEmpty(result.Tracks);

        // backup_* が除外されていること。実ライブラリには音源の複製が入った backup フォルダが存在する。
        Assert.DoesNotContain(
            result.Tracks,
            track => track.RelativePath.Contains(ScanOptions.EXCLUDED_DIRECTORY_PREFIX, StringComparison.OrdinalIgnoreCase));

        double budget = SECONDS_PER_1000_FILES * Math.Max(result.Tracks.Count, 1) / 1000.0;
        Assert.True(
            result.Elapsed.TotalSeconds <= budget,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{result.Tracks.Count} 件のスキャンに {result.Elapsed.TotalSeconds:F2} 秒かかった（上限 {budget:F2} 秒）"));
    }

    /// <summary>
    /// 実ライブラリの実態を出力する。docs/SPEC.md 5.1 の想定件数との突き合わせに使う。
    /// 断定できる値が無いため検証はせず、観測結果の記録に徹する。
    /// </summary>
    [RealLibraryFact]
    public async Task ReportsRealLibraryTagStatistics()
    {
        string root = IntegrationConst.ResolveLibraryRoot();

        ScanResult result = await new LibraryScanner(new TagReader()).ScanAsync(root);

        int missingGenre = result.Tracks.Count(track => string.IsNullOrEmpty(track.Genre));
        int nonClassicGenre = result.Tracks.Count(
            track => !string.IsNullOrEmpty(track.Genre) && track.Genre != "Classic");
        int missingComposer = result.Tracks.Count(track => string.IsNullOrEmpty(track.Composer));
        int missingConductor = result.Tracks.Count(track => string.IsNullOrEmpty(track.Conductor));
        int missingDate = result.Tracks.Count(track => string.IsNullOrEmpty(track.Date));
        int missingDisc = result.Tracks.Count(track => string.IsNullOrEmpty(track.DiscNumber));

        int splitValues = result.Tracks.Count(
            track => Enum.GetValues<TagField>().Any(track.HasMultipleValues));

        int semicolonValues = result.Tracks.Count(
            track => Enum.GetValues<TagField>()
                .SelectMany(track.GetValues)
                .Any(value => value.Contains(';', StringComparison.Ordinal)));

        int wrongConductorAtom = result.Tracks.Count(
            track => track.RawTags.ContainsKey(TagIoConst.ATOM_CONDUCTOR_WRONG));

        output.WriteLine($"総数: {result.Tracks.Count}");
        output.WriteLine($"R-102 genre 未設定: {missingGenre}");
        output.WriteLine($"R-101 genre が Classic 以外: {nonClassicGenre}");
        output.WriteLine($"R-401 composer 未設定: {missingComposer}");
        output.WriteLine($"R-402 conductor 未設定: {missingConductor}");
        output.WriteLine($"R-105 date 未設定: {missingDate}");
        output.WriteLine($"R-103 discnumber 未設定: {missingDisc}");
        output.WriteLine($"R-205 値に ; を含む: {semicolonValues}");
        output.WriteLine($"複数値として格納済み（AIMP 分割済み）: {splitValues}");
        output.WriteLine($"cond atom を持つ（AIMP から見えない指揮者）: {wrongConductorAtom}");

        Assert.NotEmpty(result.Tracks);
    }
}
