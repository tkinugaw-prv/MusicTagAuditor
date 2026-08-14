using System.Text;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Tests.Dictionary;

/// <summary>
/// 辞書編集のテスト。
/// 「検査結果から直接辞書に追加して再スキャンできる導線」（docs/SPEC.md 7.3）の中核。
/// </summary>
public sealed class DictionaryEditorTests
{
    /// <summary>
    /// 別名を足すと、その値で正規形を引けるようになることを確認する。
    /// これができないと導線が成立しない。
    /// </summary>
    [Fact]
    public void AddedAliasResolvesToCanonical()
    {
        TagDictionary before = new()
        {
            Persons = [new PersonEntry { Canonical = "Yevgeny Mravinsky", Roles = ["Conductor"] }],
        };

        Assert.False(new DictionaryIndex(before).TryResolvePerson("Evgeni Muravinsky", out _));

        TagDictionary after = DictionaryEditor.AddAlias(
            before, DictionaryCategory.Person, "Yevgeny Mravinsky", "Evgeni Muravinsky");

        Assert.True(new DictionaryIndex(after).TryResolvePerson("Evgeni Muravinsky", out PersonEntry person));
        Assert.Equal("Yevgeny Mravinsky", person.Canonical);
    }

    /// <summary>
    /// 元の辞書が書き換わらないことを確認する。
    /// 検証に落ちた編集を捨てられるよう、操作は必ず新しい辞書を返す。
    /// </summary>
    [Fact]
    public void DoesNotMutateSource()
    {
        TagDictionary before = new()
        {
            Composers = [new ComposerEntry { Canonical = "Anton Bruckner" }],
        };

        _ = DictionaryEditor.AddAlias(before, DictionaryCategory.Composer, "Anton Bruckner", "Btuckner");

        Assert.Empty(before.Composers[0].Aliases);
    }

    /// <summary>
    /// 日本語表記の別名が <c>aliasesJa</c> に入ることを確認する。
    /// </summary>
    [Fact]
    public void RoutesJapaneseAliasToAliasesJa()
    {
        TagDictionary before = new()
        {
            Persons = [new PersonEntry { Canonical = "Herbert von Karajan", Roles = ["Conductor"] }],
        };

        TagDictionary after = DictionaryEditor.AddAlias(
            before, DictionaryCategory.Person, "Herbert von Karajan", "カラヤン");

        Assert.Contains("カラヤン", after.Persons[0].AliasesJa);
        Assert.Empty(after.Persons[0].Aliases);
    }

    /// <summary>
    /// 時代分割エントリには、どの時代の正規形を指定しても別名を足せることを確認する。
    /// </summary>
    [Fact]
    public void AddsAliasToEraSplitEnsembleByAnyEraName()
    {
        TagDictionary before = new()
        {
            Ensembles =
            [
                new EnsembleEntry
                {
                    EntityId = "ru-spb-phil",
                    Eras =
                    [
                        new EnsembleEra { Until = 1991, Canonical = "Leningrad Philharmonic Orchestra" },
                        new EnsembleEra { From = 1991, Canonical = "Saint Petersburg Philharmonic Orchestra" },
                    ],
                },
            ],
        };

        TagDictionary after = DictionaryEditor.AddAlias(
            before, DictionaryCategory.Ensemble, "Saint Petersburg Philharmonic Orchestra", "Leningrad Philharmonic");

        Assert.True(new DictionaryIndex(after).TryResolveEnsemble("Leningrad Philharmonic", out EnsembleEntry ensemble));
        Assert.Equal("ru-spb-phil", ensemble.EntityId);
    }

    /// <summary>
    /// 存在しない正規形を指定したら例外になることを確認する。
    /// 黙って新規作成すると、綴りを間違えたときに重複エントリができる。
    /// </summary>
    [Fact]
    public void ThrowsWhenCanonicalNotFound()
    {
        TagDictionary dictionary = new();

        Assert.Throws<InvalidOperationException>(() =>
            DictionaryEditor.AddAlias(dictionary, DictionaryCategory.Person, "Nobody", "someone"));
    }

    /// <summary>
    /// 新規の団体を足すと索引から引けることを確認する。
    /// </summary>
    [Fact]
    public void AddsNewEnsemble()
    {
        TagDictionary after = DictionaryEditor.AddEnsemble(
            new TagDictionary(), "jp-nhk-so", "NHK Symphony Orchestra", ["NHK交響楽団"]);

        Assert.True(new DictionaryIndex(after).TryResolveEnsemble("NHK交響楽団", out EnsembleEntry ensemble));
        Assert.Equal("jp-nhk-so", ensemble.EntityId);
        Assert.Contains("NHK交響楽団", ensemble.AliasesJa);
    }

    /// <summary>
    /// 正規形と同じ値の別名は落ちることを確認する。索引を無駄に膨らませない。
    /// </summary>
    [Fact]
    public void DropsAliasIdenticalToCanonical()
    {
        TagDictionary after = DictionaryEditor.AddComposer(
            new TagDictionary(), "Anton Bruckner", ["Anton Bruckner", "anton  bruckner"]);

        Assert.Empty(after.Composers[0].Aliases);
    }

    /// <summary>
    /// 検出フィールドから種別を推定できることを確認する（docs/TAGGING_POLICY.md 2.1）。
    /// </summary>
    [Theory]
    [InlineData(TagField.Composer, DictionaryCategory.Composer)]
    [InlineData(TagField.Artist, DictionaryCategory.Person)]
    [InlineData(TagField.Conductor, DictionaryCategory.Person)]
    [InlineData(TagField.AlbumArtist, DictionaryCategory.Ensemble)]
    public void SuggestsCategoryFromField(TagField field, DictionaryCategory expected)
    {
        Assert.Equal(expected, DictionaryEditor.SuggestCategory(field));
    }

    /// <summary>
    /// 実体 ID の候補が既存と重複しないことを確認する。
    /// </summary>
    [Fact]
    public void SuggestsUniqueEntityId()
    {
        TagDictionary dictionary = new()
        {
            Ensembles = [new EnsembleEntry { EntityId = "nhk-symphony-orchestra", Canonical = "x" }],
        };

        Assert.Equal("nhk-symphony-orchestra-2", DictionaryEditor.SuggestEntityId(dictionary, "NHK Symphony Orchestra"));
    }

    /// <summary>
    /// 足した作品を、その別名で引けるようになることを確認する。
    /// 検査結果からの導線（docs/SPEC.md 7.3.2）はこれが成り立たないと成立しない。
    /// </summary>
    [Fact]
    public void AddedWorkResolvesByAlias()
    {
        TagDictionary before = new()
        {
            Composers = [new ComposerEntry { Canonical = "Anton Bruckner" }],
        };

        TagDictionary after = DictionaryEditor.AddWork(
            before, "Anton Bruckner", "Symphony No. 8", ["Bruckner Sym.8", "ブルックナー 8"]);

        WorkEntry added = Assert.Single(after.Works);

        Assert.Equal(["Bruckner Sym.8"], added.Aliases);
        Assert.Equal(["ブルックナー 8"], added.AliasesJa);

        DictionaryIndex index = new(after);

        Assert.True(index.TryResolveWork("Anton Bruckner", "ブルックナー 8", out WorkEntry work));
        Assert.Equal("Symphony No. 8", work.Canonical);

        // 作曲家が違えば別の作品。作曲家で絞らずに引けてはならない（7.4.3 手順3）。
        Assert.False(index.TryResolveWork("Johannes Brahms", "ブルックナー 8", out _));
    }

    /// <summary>
    /// 同じ作曲家 + 作品名なら行を増やさず別名だけを足すことを確認する。
    /// 自然キーが重複した作品は索引が先勝ちで捨てるため、行を増やしてはならない（7.4.1）。
    /// </summary>
    [Fact]
    public void MergesAliasesIntoExistingWork()
    {
        TagDictionary before = new()
        {
            Composers = [new ComposerEntry { Canonical = "Anton Bruckner" }],
            Works =
            [
                new WorkEntry
                {
                    Composer = "Anton Bruckner",
                    Canonical = "Symphony No. 8",
                    Aliases = ["Symphony No.8"],
                },
            ],
        };

        TagDictionary after = DictionaryEditor.AddWork(
            before, "Anton Bruckner", "Symphony No. 8", ["Symphony No.8", "Sinfonie Nr. 8"]);

        WorkEntry merged = Assert.Single(after.Works);

        Assert.Equal(["Symphony No.8", "Sinfonie Nr. 8"], merged.Aliases);
        Assert.Equal(["Symphony No.8"], before.Works[0].Aliases);
    }

    /// <summary>
    /// 個別例外を足すと、そのフォルダで引けるようになることを確認する。
    /// </summary>
    [Fact]
    public void AddedAlbumOverrideResolvesByFolder()
    {
        TagDictionary after = DictionaryEditor.AddAlbumOverride(
            new TagDictionary(),
            new AlbumOverrideEntry
            {
                Folder = Path.Combine("その他", "トスカニーニ インター"),
                Exclude = true,
                Note = "3.5 規則6 コンピレーション",
            });

        DictionaryIndex index = new(after);

        Assert.True(index.TryResolveAlbumOverride(Path.Combine("その他", "トスカニーニ インター"), 1, out AlbumOverrideEntry entry));
        Assert.True(entry.Exclude);
    }

    /// <summary>
    /// 同じフォルダ + disc の個別例外は置き換えることを確認する。
    ///
    /// **Unicode の正規化形が違っても同じフォルダとして扱う。** 濁点付きの仮名は NFC と NFD の
    /// どちらでも保存でき、見た目が同じでも文字列としては一致しない（docs/SPEC.md 7.4.5）。
    /// 2 件になると先に見つかったほうしか効かず、直したつもりの内容が反映されない。
    /// </summary>
    [Fact]
    public void ReplacesAlbumOverrideForSameFolderAndDisc()
    {
        string nfc = Path.Combine("リムスキー・コルサコフ", "シェエラザード").Normalize(NormalizationForm.FormC);
        string nfd = nfc.Normalize(NormalizationForm.FormD);

        TagDictionary before = DictionaryEditor.AddAlbumOverride(
            new TagDictionary(),
            new AlbumOverrideEntry { Folder = nfc, Disc = 1, Exclude = true, Note = "最初の理由" });

        TagDictionary after = DictionaryEditor.AddAlbumOverride(
            before,
            new AlbumOverrideEntry { Folder = nfd, Disc = 1, WorkName = "Scheherazade", Note = "3.5 規則4 版の違い" });

        AlbumOverrideEntry entry = Assert.Single(after.AlbumOverrides);

        Assert.Equal("Scheherazade", entry.WorkName);
        Assert.False(entry.Exclude);
    }

    /// <summary>
    /// disc が違う個別例外は別の項目として足すことを確認する。
    /// </summary>
    [Fact]
    public void KeepsAlbumOverridesForDifferentDiscs()
    {
        TagDictionary before = DictionaryEditor.AddAlbumOverride(
            new TagDictionary(),
            new AlbumOverrideEntry { Folder = "ワーグナー", Disc = 1, WorkName = "Die Walküre", Note = "1 枚目" });

        TagDictionary after = DictionaryEditor.AddAlbumOverride(
            before,
            new AlbumOverrideEntry { Folder = "ワーグナー", Disc = 2, WorkName = "Siegfried", Note = "2 枚目" });

        Assert.Equal(2, after.AlbumOverrides.Count);
    }

    /// <summary>
    /// フォルダ名からの別名候補が、作曲家フォルダを飛ばして演奏者を落とした形も出すことを確認する
    /// （docs/SPEC.md 7.3.2）。
    ///
    /// 全体でしか引かないと、同じ作品が演奏者の数だけ別のエイリアスになる。
    /// </summary>
    [Fact]
    public void SuggestsFolderAliasesWithoutComposerSegment()
    {
        DictionaryIndex index = new(new TagDictionary
        {
            Composers = [new ComposerEntry { Canonical = "Anton Bruckner", AliasesJa = ["ブルックナー"] }],
        });

        IReadOnlyList<string> candidates = DictionaryEditor.SuggestWorkAliases(
            Path.Combine("ブルックナー", "ブルックナー 8 - Wand"), index);

        Assert.Equal(["ブルックナー 8 - Wand", "ブルックナー 8"], candidates);
    }

    /// <summary>
    /// 既に辞書にある値を検出できることを確認する。二重登録を防ぐための確認。
    /// </summary>
    [Fact]
    public void DetectsAlreadyKnownValue()
    {
        DictionaryIndex index = new(new TagDictionary
        {
            Persons = [new PersonEntry { Canonical = "Karl Böhm", Roles = ["Conductor"], Aliases = ["Bohm"] }],
        });

        Assert.True(DictionaryEditor.IsAlreadyKnown(index, "bohm", out string owner));
        Assert.Contains("Karl Böhm", owner, StringComparison.Ordinal);
        Assert.False(DictionaryEditor.IsAlreadyKnown(index, "Various Artists", out _));
    }

    /// <summary>
    /// 既にある別名と同じ正規化キーになる別名を足さないことを確認する。
    ///
    /// 足しても索引は先勝ちで捨てるため引けるようにはならず、検証の警告だけが増える。
    /// </summary>
    [Fact]
    public void DoesNotAddAliasCollidingWithExistingAlias()
    {
        TagDictionary before = new()
        {
            Persons = [new PersonEntry { Canonical = "Karl Böhm", Roles = ["Conductor"], Aliases = ["Bohm"] }],
        };

        TagDictionary after = DictionaryEditor.AddAlias(before, DictionaryCategory.Person, "Karl Böhm", "Böhm");

        Assert.Equal(["Bohm"], after.Persons[0].Aliases);
        Assert.Empty(DictionaryValidator.Validate(after));
    }

    /// <summary>
    /// 正規形と同じ正規化キーになる別名を足さないことを確認する。
    /// </summary>
    [Fact]
    public void DoesNotAddAliasCollidingWithCanonical()
    {
        TagDictionary before = new()
        {
            Composers = [new ComposerEntry { Canonical = "Antonín Dvořák" }],
        };

        TagDictionary after = DictionaryEditor.AddAlias(
            before, DictionaryCategory.Composer, "Antonín Dvořák", "Antonin Dvorak");

        Assert.Empty(after.Composers[0].Aliases);
    }

    /// <summary>
    /// 既存の作品に別名を足すとき、正規化キーが同じものを足さないことを確認する。
    ///
    /// <c>Symphony No.7</c> と <c>Symphony No. 7</c> は辞書では同じキーになる。
    /// 作品は番号で呼ぶものが大半で、この形の重複がもっとも溜まりやすい。
    /// </summary>
    [Fact]
    public void DoesNotAddWorkAliasCollidingWithCanonical()
    {
        TagDictionary before = DictionaryEditor.AddWork(
            new TagDictionary { Composers = [new ComposerEntry { Canonical = "Anton Bruckner" }] },
            "Anton Bruckner",
            "Symphony No. 7",
            ["Bruckner 7"]);

        TagDictionary after = DictionaryEditor.AddWork(
            before, "Anton Bruckner", "Symphony No. 7", ["Symphony No.7", "ブルックナー 7"]);

        Assert.Equal(["Bruckner 7"], after.Works[0].Aliases);
        Assert.Equal(["ブルックナー 7"], after.Works[0].AliasesJa);
        Assert.Empty(DictionaryValidator.Validate(after));
    }
}
