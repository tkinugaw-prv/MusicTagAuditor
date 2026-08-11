using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.Core.Tests.Dictionary;

/// <summary>
/// 辞書の構成をログとテスト出力に残すための要約のテスト。
///
/// **要約が落ちると、検出結果が想定と違うときの切り分けができなくなる。**
/// 実際に「別の辞書を読んでいた」ことに気づけず、R-504 が全件保留になる原因の特定に
/// 時間がかかった（2026-08-12）。
/// </summary>
public sealed class DictionarySummaryTests
{
    /// <summary>
    /// 作品と個別例外の件数が出ることを確認する。
    /// **この 2 つが要約の目的である。** 同梱の既定辞書には入らないため、
    /// 読んでいる辞書が本体と同じかどうかはこの数字でしか分からない。
    /// </summary>
    [Fact]
    public void IncludesWorksAndOverrides()
    {
        string summary = DictionarySummary.Describe(new TagDictionary
        {
            Version = 3,
            Composers = [new ComposerEntry { Canonical = "Anton Bruckner" }],
            Works = [new WorkEntry { Composer = "Anton Bruckner", Canonical = "Symphony No. 8" }],
            AlbumOverrides = [new AlbumOverrideEntry { Folder = "x", Exclude = true }],
        });

        Assert.Contains("版=3", summary, StringComparison.Ordinal);
        Assert.Contains("作曲家=1", summary, StringComparison.Ordinal);
        Assert.Contains("作品=1", summary, StringComparison.Ordinal);
        Assert.Contains("個別例外=1", summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// 項目が null でも落ちずに 0 と出ることを確認する。
    ///
    /// JSON に <c>"works": null</c> と書かれているとプロパティ初期化子ではなく null が入る。
    /// 本体が書いた辞書に実在した形なので、要約で落ちると起動できなくなる。
    /// </summary>
    [Fact]
    public void TreatsNullSectionsAsZero()
    {
        string summary = DictionarySummary.Describe(new TagDictionary
        {
            Composers = null!,
            Works = null!,
            AlbumOverrides = null!,
        });

        Assert.Contains("作曲家=0", summary, StringComparison.Ordinal);
        Assert.Contains("作品=0", summary, StringComparison.Ordinal);
        Assert.Contains("個別例外=0", summary, StringComparison.Ordinal);
    }

    /// <summary>
    /// 同梱の既定辞書には作品エントリが入っていないことを確認する（docs/SPEC.md 13章 D5）。
    /// 所蔵に完全に依存するため、他の所蔵では無意味な値になる。
    /// </summary>
    [Fact]
    public void BundledDictionaryHasNoWorks()
    {
        TagDictionary bundled = DictionaryLoader.LoadDefault();

        Assert.Empty(bundled.Works ?? []);
        Assert.Empty(bundled.AlbumOverrides ?? []);
        Assert.NotEmpty(bundled.Composers);
    }
}
