using MusicTagAuditor.Core.Editing;
using MusicTagAuditor.Core.Export;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Tests.Export;

/// <summary>
/// ファイル一覧の CSV 出力のテスト。
///
/// 重点は 2 つ。**画面に見えている値がそのまま出ること**（＝保留中の手編集が反映されること）と、
/// 実データのパス・アルバム名に含まれる読点・引用符・非 ASCII で列がずれないこと。
/// </summary>
public sealed class TrackCsvExporterTests
{
    /// <summary>
    /// 見出しにファイルの情報とタグの両方が並ぶことを確認する。
    /// </summary>
    [Fact]
    public void IncludesFileAndTagColumns()
    {
        string header = TrackCsvExporter.Build([], new ManualEditSet()).Split('\n')[0];

        Assert.Contains("\"パス\"", header, StringComparison.Ordinal);
        Assert.Contains("\"形式\"", header, StringComparison.Ordinal);
        Assert.Contains("\"編集\"", header, StringComparison.Ordinal);
        Assert.Contains("\"複数値\"", header, StringComparison.Ordinal);
        Assert.Contains("\"作曲家\"", header, StringComparison.Ordinal);
        Assert.Contains("\"コメント\"", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// 編集していない行では、読み取った値がそのまま出ることを確認する。
    /// </summary>
    [Fact]
    public void WritesStoredValuesWhenNotEdited()
    {
        TrackTags track = Track(
            "ブルックナー/ブル8 - ショルティ/01.flac",
            (TagField.Composer, ["Anton Bruckner"]),
            (TagField.Title, ["Allegro moderato"]));

        string line = TrackCsvExporter.Build([track], new ManualEditSet()).Split('\n')[1];

        Assert.Contains("\"Anton Bruckner\"", line, StringComparison.Ordinal);
        Assert.Contains("\"Allegro moderato\"", line, StringComparison.Ordinal);
        Assert.DoesNotContain("\"○\"", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// 保留中の手編集が反映されることを確認する。
    /// **画面と CSV で値が違うと、どちらが本当か確かめられない。**
    /// </summary>
    [Fact]
    public void ReflectsPendingEdits()
    {
        TrackTags track = Track("01.m4a", (TagField.Composer, ["Btuckner"]));

        ManualEditSet edits = new();
        edits.Set(track, TagField.Composer, "Anton Bruckner");

        string line = TrackCsvExporter.Build([track], edits).Split('\n')[1];

        Assert.Contains("\"Anton Bruckner\"", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Btuckner", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// 編集した行に印が付くことを確認する。適用前の CSV であることが読み取れないと、
    /// 書き戻し済みの記録と取り違える。
    /// </summary>
    [Fact]
    public void MarksEditedTracks()
    {
        TrackTags edited = Track("01.m4a", (TagField.Genre, ["Classic"]));
        TrackTags untouched = Track("02.m4a", (TagField.Genre, ["Classic"]));

        ManualEditSet edits = new();
        edits.Set(edited, TagField.Genre, "Classical");

        string[] lines = TrackCsvExporter.Build([edited, untouched], edits).Split('\n');

        Assert.Contains("\"○\"", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain("\"○\"", lines[2], StringComparison.Ordinal);
    }

    /// <summary>
    /// 複数値として格納されている行に印が付くことを確認する。
    /// 値は連結されて 1 つに見えるため、印が無いと区別できない（docs/TAGGING_POLICY.md 3.4）。
    /// </summary>
    [Fact]
    public void MarksTracksWithSplitValues()
    {
        TrackTags split = Track("01.m4a", (TagField.AlbumArtist, ["Kirov Orchestra", "Mariinsky Theatre"]));

        string line = TrackCsvExporter.Build([split], new ManualEditSet()).Split('\n')[1];

        Assert.Contains("\"○\"", line, StringComparison.Ordinal);
        Assert.Contains("\"Kirov Orchestra; Mariinsky Theatre\"", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// 読点を含む値が列をまたがないことを確認する。
    /// </summary>
    [Fact]
    public void QuotesValuesContainingComma()
    {
        TrackTags track = Track(
            "ショスタコーヴィチ/ショス4 - ゲルギエフ/01.flac",
            (TagField.AlbumArtist, ["Kirov Orchestra, Mariinsky Theatre"]));

        string[] lines = TrackCsvExporter.Build([track], new ManualEditSet()).Split('\n');

        Assert.Contains("\"Kirov Orchestra, Mariinsky Theatre\"", lines[1], StringComparison.Ordinal);

        // 見出しと同じ列数であること。読点で列がずれていない。
        Assert.Equal(CountCells(lines[0]), CountCells(lines[1]));
    }

    /// <summary>
    /// 引用符を含む値が二重化されることを確認する。
    /// </summary>
    [Fact]
    public void EscapesDoubleQuotes()
    {
        TrackTags track = Track("01.m4a", (TagField.Album, ["Piano Concerto No.5 \"EMPEROR\""]));

        string csv = TrackCsvExporter.Build([track], new ManualEditSet());

        Assert.Contains("\"Piano Concerto No.5 \"\"EMPEROR\"\"\"", csv, StringComparison.Ordinal);
    }

    /// <summary>
    /// フォルダ列に相対パスのフォルダ部分が出ることを確認する。
    /// ルート直下は空のまま出す（<c>(root)</c> は画面だけの読み替え。docs/SPEC.md 5.2）。
    /// </summary>
    [Fact]
    public void SplitsFolderAndFileName()
    {
        string[] lines = TrackCsvExporter.Build(
            [Track(Path.Combine("ブルックナー", "01.flac")), Track("02.flac")],
            new ManualEditSet()).Split('\n');

        Assert.StartsWith(
            "\"" + Path.Combine("ブルックナー", "01.flac") + "\",\"ブルックナー\",\"01.flac\"",
            lines[1],
            StringComparison.Ordinal);

        Assert.StartsWith("\"02.flac\",\"\",\"02.flac\"", lines[2], StringComparison.Ordinal);
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
            TrackCsvExporter.WriteFile(
                path,
                [Track("ブルックナー/01.flac", (TagField.Genre, ["Classic"]))],
                new ManualEditSet());

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
    /// 行のセル数を数える。
    /// </summary>
    private static int CountCells(string line)
    {
        return line.Count(c => c == '"') / 2;
    }

    /// <summary>
    /// テスト用のタグを作る。
    /// </summary>
    private static TrackTags Track(
        string relativePath,
        params (TagField Field, IReadOnlyList<string> Values)[] fields)
    {
        return new TrackTags
        {
            RelativePath = relativePath,
            FullPath = Path.Combine("D:", "Music", relativePath),
            Format = AudioFormat.M4a,
            Fields = TrackTags.BuildFields(
                fields.Select(field => new KeyValuePair<TagField, IReadOnlyList<string>>(field.Field, field.Values))),
            RawTags = new Dictionary<string, string[]>(),
        };
    }
}
