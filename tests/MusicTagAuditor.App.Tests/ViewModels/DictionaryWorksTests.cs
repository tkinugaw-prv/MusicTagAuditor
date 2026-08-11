using System.IO;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// 辞書タブの works / albumOverrides のテスト（docs/SPEC.md 7.3.1）。
///
/// 段階 8 で辞書に入れた作品エントリと個別例外は、この 2 つのタブが無いあいだ
/// アプリからは見ることも直すこともできなかった。**読めて、直せて、保存で失われない**の 3 点を守る。
/// </summary>
public sealed class DictionaryWorksTests : IDisposable
{
    /// <summary>テスト用の作業ディレクトリ。辞書をここに置く。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MusicTagAuditor-dict-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 作業ディレクトリを用意する。
    /// </summary>
    public DictionaryWorksTests()
    {
        Directory.CreateDirectory(_directory);
    }

    /// <summary>
    /// 辞書の作品と個別例外が編集行として読み込まれることを確認する。
    /// </summary>
    [Fact]
    public void LoadsWorksAndAlbumOverrides()
    {
        DictionaryViewModel viewModel = new(CreateStore());

        WorkRowViewModel work = Assert.Single(viewModel.Works);

        Assert.Equal("Anton Bruckner", work.Composer);
        Assert.Equal("Symphony No. 8", work.Canonical);
        Assert.Equal("ブルックナー 8", work.AliasesJaText);

        AlbumOverrideRowViewModel entry = Assert.Single(viewModel.AlbumOverrides);

        Assert.Equal("その他", entry.Folder);
        Assert.Equal("2", entry.DiscText);
        Assert.True(entry.Exclude);
        Assert.Equal("3.5 規則6 コンピレーション", entry.Note);
    }

    /// <summary>
    /// 保存しても作品と個別例外が失われないことを確認する。
    ///
    /// **他のタブの編集で消えては困る。** 編集行から辞書を組み立て直す作りなので、
    /// 組み立てから漏れた種別は保存のたびに黙って消える。
    /// </summary>
    [Fact]
    public void KeepsWorksAndAlbumOverridesOnSave()
    {
        DictionaryStore store = CreateStore();
        DictionaryViewModel viewModel = new(store);

        viewModel.Works[0].Canonical = "Symphony No. 9";
        viewModel.SaveCommand.Execute(null);

        Assert.False(viewModel.IsDirty);

        WorkEntry work = Assert.Single(store.Dictionary.Works);

        Assert.Equal("Symphony No. 9", work.Canonical);
        Assert.Equal("Anton Bruckner", work.Composer);

        AlbumOverrideEntry entry = Assert.Single(store.Dictionary.AlbumOverrides);

        Assert.Equal(2, entry.Disc);
        Assert.True(entry.Exclude);
    }

    /// <summary>
    /// 空欄の disc が「そのフォルダの全ディスク」として保存されることを確認する（docs/SPEC.md 7.4.5）。
    /// 0 や 1 に丸めると、意図した範囲より狭い例外になる。
    /// </summary>
    [Fact]
    public void TreatsBlankDiscAsWholeFolder()
    {
        DictionaryStore store = CreateStore();
        DictionaryViewModel viewModel = new(store);

        viewModel.AlbumOverrides[0].DiscText = string.Empty;
        viewModel.SaveCommand.Execute(null);

        Assert.Null(Assert.Single(store.Dictionary.AlbumOverrides).Disc);
    }

    /// <summary>
    /// 作曲家の候補が、作品が現に名乗っている作曲家を落とさないことを確認する（docs/SPEC.md 7.3.1）。
    ///
    /// 候補から消えると選択欄は「選択なし」に落ち、編集していない行の作曲家まで黙って失われる。
    /// 正規形と一致しない作曲家は保存時の検証がエラーとして出す。
    /// </summary>
    [Fact]
    public void KeepsComposerOfExistingWorkInCandidates()
    {
        DictionaryViewModel viewModel = new(CreateStore());

        Assert.Contains("Anton Bruckner", viewModel.ComposerCanonicals);

        viewModel.Composers[0].Canonical = "Anton Bruckner (改名中)";

        Assert.Contains("Anton Bruckner", viewModel.ComposerCanonicals);
        Assert.Contains("Anton Bruckner (改名中)", viewModel.ComposerCanonicals);
    }

    /// <summary>
    /// 作曲家の正規形と一致しない作品が保存を止めることを確認する。
    /// 一致しない作品は索引に載らず、登録しても引けない（docs/SPEC.md 7.4.1）。
    /// </summary>
    [Fact]
    public void BlocksSaveWhenWorkComposerIsUnknown()
    {
        DictionaryStore store = CreateStore();
        DictionaryViewModel viewModel = new(store);

        viewModel.Works[0].Composer = "Anton Buruckner";
        viewModel.ValidateCommand.Execute(null);

        Assert.True(DictionaryValidator.HasError(viewModel.Issues));
    }

    /// <summary>
    /// 作業ディレクトリを片付ける。
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // 掃除に失敗してもテストの結果は変えない。
        }
    }

    /// <summary>
    /// 作品と個別例外を持つ辞書のストアを作る。
    /// </summary>
    /// <returns>ストア。</returns>
    private DictionaryStore CreateStore()
    {
        DictionaryStore store = new(_directory);

        store.Save(store.Dictionary with
        {
            Composers = [new ComposerEntry { Canonical = "Anton Bruckner", AliasesJa = ["ブルックナー"] }],
            Persons = [],
            Ensembles = [],
            Works =
            [
                new WorkEntry
                {
                    Composer = "Anton Bruckner",
                    Canonical = "Symphony No. 8",
                    Aliases = ["Symphony No.8"],
                    AliasesJa = ["ブルックナー 8"],
                },
            ],
            AlbumOverrides =
            [
                new AlbumOverrideEntry
                {
                    Folder = "その他",
                    Disc = 2,
                    Exclude = true,
                    Note = "3.5 規則6 コンピレーション",
                },
            ],
        });

        return store;
    }
}
