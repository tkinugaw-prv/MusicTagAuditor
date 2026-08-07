using MusicTagAuditor.Core.Export;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Tests.Export;

/// <summary>
/// CSV 出力のテスト。
/// 実データのパスやアルバム名には読点・引用符・非 ASCII が含まれるため、そこを重点的に確認する。
/// </summary>
public sealed class ChangeCsvExporterTests
{
    /// <summary>
    /// 見出しに根拠列が含まれることを確認する。
    /// 根拠が読めない差分は承認できないという要件は CSV でも変わらない。
    /// </summary>
    [Fact]
    public void IncludesRationaleColumn()
    {
        string csv = ChangeCsvExporter.Build([]);

        Assert.Contains("\"根拠\"", csv, StringComparison.Ordinal);
    }

    /// <summary>
    /// 読点を含む値が列をまたがないことを確認する。
    /// </summary>
    [Fact]
    public void QuotesValuesContainingComma()
    {
        string csv = ChangeCsvExporter.Build(
        [
            Change(
                "ショスタコーヴィチ/ショス4 - ゲルギエフ/01.flac",
                TagField.AlbumArtist,
                ["Kirov Orchestra, Mariinsky Theatre"],
                ["Kirov Orchestra"]),
        ]);

        string line = csv.Split('\n')[1];

        Assert.Contains("\"Kirov Orchestra, Mariinsky Theatre\"", line, StringComparison.Ordinal);

        // 見出しと同じ列数であること。読点で列がずれていない。
        Assert.Equal(CountCells(csv.Split('\n')[0]), CountCells(line));
    }

    /// <summary>
    /// 引用符を含む値が二重化されることを確認する。
    /// </summary>
    [Fact]
    public void EscapesDoubleQuotes()
    {
        string csv = ChangeCsvExporter.Build(
        [
            Change("01.m4a", TagField.Album, ["Piano Concerto No.5 \"EMPEROR\""], ["Beethoven: Piano Concerto No.5"]),
        ]);

        Assert.Contains("\"Piano Concerto No.5 \"\"EMPEROR\"\"\"", csv, StringComparison.Ordinal);
    }

    /// <summary>
    /// 適用予定かどうかが分かることを確認する。
    /// </summary>
    [Fact]
    public void MarksSelectedChanges()
    {
        TagChange selected = Change("01.m4a", TagField.Genre, [], ["Classic"]);
        TagChange unselected = Change("02.m4a", TagField.Genre, [], ["Classic"]);
        unselected.IsSelected = false;

        string[] lines = ChangeCsvExporter.Build([selected, unselected]).Split('\n');

        Assert.Contains("\"○\"", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain("\"○\"", lines[2], StringComparison.Ordinal);
    }

    /// <summary>
    /// 保留の項目が区分から判別できることを確認する。
    /// </summary>
    [Fact]
    public void ShowsHoldClassification()
    {
        TagChange held = new(
            "01.m4a",
            TagField.AlbumArtist,
            ["Leningrad Philharmonic"],
            [],
            "R-209",
            "date が空欄のため保留",
            Severity.Error,
            HoldReason.EraUnknown);

        Assert.Contains("\"保留\"", ChangeCsvExporter.Build([held]), StringComparison.Ordinal);
    }

    /// <summary>
    /// BOM 付き UTF-8 で書き出すことを確認する。Excel が日本語を誤認しないため。
    /// </summary>
    [Fact]
    public void WritesFileWithUtf8Bom()
    {
        string path = Path.Combine(Path.GetTempPath(), $"MusicTagAuditor.tests.{Guid.NewGuid():N}.csv");

        try
        {
            ChangeCsvExporter.WriteFile(path, [Change("ブルックナー/01.flac", TagField.Genre, [], ["Classic"])]);

            byte[] bytes = File.ReadAllBytes(path);

            Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
            Assert.Contains("ブルックナー", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// 集計 CSV がルール別の件数を持つことを確認する。
    /// </summary>
    [Fact]
    public void BuildsSummaryByRule()
    {
        string csv = ChangeCsvExporter.BuildSummary(
        [
            Change("01.m4a", TagField.Genre, [], ["Classic"], "R-102"),
            Change("02.m4a", TagField.Genre, [], ["Classic"], "R-102"),
            Change("03.m4a", TagField.Composer, ["Btuckner"], ["Anton Bruckner"], "R-201"),
        ]);

        Assert.Contains("\"R-102\",\"2\",\"2\",\"0\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"R-201\",\"1\",\"1\",\"0\"", csv, StringComparison.Ordinal);
    }

    /// <summary>
    /// 行のセル数を数える。
    /// </summary>
    private static int CountCells(string line)
    {
        return line.Count(c => c == '"') / 2;
    }

    /// <summary>
    /// テスト用の差分を作る。
    /// </summary>
    private static TagChange Change(
        string relativePath,
        TagField field,
        string[] before,
        string[] after,
        string ruleId = "R-101")
    {
        return new TagChange(relativePath, field, before, after, ruleId, "テスト用の根拠", Severity.Error);
    }
}
