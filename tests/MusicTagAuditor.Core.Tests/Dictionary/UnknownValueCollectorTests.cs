using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Tests.Dictionary;

/// <summary>
/// 未知の値の集約のテスト。
/// 検査結果タブから辞書に足す導線の入力になるため、値単位でまとまることを重点的に確認する。
/// </summary>
public sealed class UnknownValueCollectorTests
{
    /// <summary>
    /// 同じ値が複数ファイルに散っていても 1 行にまとまることを確認する。
    /// 登録作業は 1 回で済むので、明細のまま並べる意味がない。
    /// </summary>
    [Fact]
    public void GroupsSameValueAcrossFiles()
    {
        IReadOnlyList<UnknownValue> unknowns = UnknownValueCollector.Collect(
        [
            Unknown("a/01.m4a", TagField.AlbumArtist, "Various Artists"),
            Unknown("a/02.m4a", TagField.AlbumArtist, "Various Artists"),
            Unknown("b/01.m4a", TagField.AlbumArtist, "Various Artists"),
        ]);

        UnknownValue single = Assert.Single(unknowns);

        Assert.Equal("Various Artists", single.Value);
        Assert.Equal(3, single.Count);
        Assert.Equal(DictionaryCategory.Ensemble, single.Category);
    }

    /// <summary>
    /// <c>artist</c> と <c>conductor</c> はどちらも人物なので 1 行にまとまることを確認する。
    /// </summary>
    [Fact]
    public void MergesArtistAndConductorIntoOnePersonRow()
    {
        IReadOnlyList<UnknownValue> unknowns = UnknownValueCollector.Collect(
        [
            Unknown("a/01.mp3", TagField.Artist, "Unknown Performer"),
            Unknown("a/01.mp3", TagField.Conductor, "Unknown Performer"),
        ]);

        UnknownValue single = Assert.Single(unknowns);

        Assert.Equal(DictionaryCategory.Person, single.Category);
        Assert.Equal(2, single.Fields.Count);

        // 同じファイルの 2 フィールドなので、件数はファイル数の 1。
        Assert.Equal(1, single.Count);
    }

    /// <summary>
    /// 同じ文字列でも <c>albumartist</c> は団体として別行になることを確認する。
    /// 人物として登録するか団体として登録するかは別の判断になる。
    /// </summary>
    [Fact]
    public void SeparatesEnsembleFromPersonForSameText()
    {
        IReadOnlyList<UnknownValue> unknowns = UnknownValueCollector.Collect(
        [
            Unknown("a/01.m4a", TagField.Artist, "Someone"),
            Unknown("a/01.m4a", TagField.AlbumArtist, "Someone"),
        ]);

        Assert.Equal(2, unknowns.Count);
        Assert.Contains(unknowns, unknown => unknown.Category == DictionaryCategory.Person);
        Assert.Contains(unknowns, unknown => unknown.Category == DictionaryCategory.Ensemble);
    }

    /// <summary>
    /// 修正案を持つ差分は対象外であることを確認する。辞書に載っているということだから。
    /// </summary>
    [Fact]
    public void IgnoresChangesThatAlreadyHaveFix()
    {
        IReadOnlyList<UnknownValue> unknowns = UnknownValueCollector.Collect(
        [
            new TagChange("a/01.m4a", TagField.Artist, ["Solt"], ["Georg Solti"], "R-202", "根拠", Severity.Error),
        ]);

        Assert.Empty(unknowns);
    }

    /// <summary>
    /// 保留（<c>HOLD_ERA_UNKNOWN</c>）は対象外であることを確認する。
    /// 辞書ではなく <c>date</c> が埋まれば解決するため、辞書に足しても意味がない。
    /// </summary>
    [Fact]
    public void IgnoresHeldChanges()
    {
        IReadOnlyList<UnknownValue> unknowns = UnknownValueCollector.Collect(
        [
            new TagChange(
                "a/01.m4a",
                TagField.AlbumArtist,
                ["Leningrad Philharmonic"],
                [],
                "R-202",
                "根拠",
                Severity.Error,
                HoldReason.EraUnknown),
        ]);

        Assert.Empty(unknowns);
    }

    /// <summary>
    /// 辞書と関係のないルールを拾わないことを確認する。
    /// R-203 / R-204 の「指揮者を特定できない」は辞書登録では解決しない。
    /// </summary>
    [Fact]
    public void IgnoresRulesUnrelatedToDictionaryLookup()
    {
        IReadOnlyList<UnknownValue> unknowns = UnknownValueCollector.Collect(
        [
            new TagChange("a/01.m4a", TagField.Artist, ["Richard Wagner"], [], "R-203", "根拠", Severity.Error),
        ]);

        Assert.Empty(unknowns);
    }

    /// <summary>
    /// 件数の多い順に並ぶことを確認する。効果の大きいものから片付けられるようにする。
    /// </summary>
    [Fact]
    public void OrdersByCountDescending()
    {
        IReadOnlyList<UnknownValue> unknowns = UnknownValueCollector.Collect(
        [
            Unknown("a/01.m4a", TagField.AlbumArtist, "Rare"),
            Unknown("b/01.m4a", TagField.AlbumArtist, "Common"),
            Unknown("b/02.m4a", TagField.AlbumArtist, "Common"),
        ]);

        Assert.Equal("Common", unknowns[0].Value);
        Assert.Equal("Rare", unknowns[1].Value);
    }

    /// <summary>
    /// 辞書に無い値としての差分を作る。
    /// </summary>
    private static TagChange Unknown(string relativePath, TagField field, string value)
    {
        return new TagChange(
            relativePath,
            field,
            [value],
            [],
            "R-202",
            "辞書に無い値。正規形を辞書に登録してから再スキャンする",
            Severity.Info);
    }
}
