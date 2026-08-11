using System.IO;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// 「作品を辞書に追加」ダイアログのテスト（docs/SPEC.md 7.3.2）。
///
/// 守りたいのは 2 点。**作品名は機械が埋めない**（現在の <c>album</c> は誤っていることがある）ことと、
/// **別名の候補が単位の album 値とフォルダ名から出る**こと。候補が出なければ、結局は手打ちになる。
/// </summary>
public sealed class AddWorkViewModelTests
{
    /// <summary>
    /// 作品名は空で出し、入るまで登録させないことを確認する。
    /// </summary>
    [Fact]
    public void KeepsCanonicalEmptyUntilEntered()
    {
        AddWorkViewModel viewModel = Create();

        Assert.Equal(string.Empty, viewModel.Canonical);
        Assert.False(viewModel.CanApply(out string reason));
        Assert.Contains("作品名", reason, StringComparison.Ordinal);

        viewModel.Canonical = "Symphony No. 8";

        Assert.True(viewModel.CanApply(out _));
    }

    /// <summary>
    /// 別名の候補に album の値とフォルダ名（演奏者を落とした形も）が入ることを確認する。
    /// 作曲家フォルダは作品名ではないので候補にしない。
    /// </summary>
    [Fact]
    public void OffersAliasCandidatesFromAlbumAndFolder()
    {
        AddWorkViewModel viewModel = Create();

        Assert.Equal(
            ["Bruckner Symphony No.8", "ブルックナー 8 - ショルティ", "ブルックナー 8"],
            viewModel.Candidates.Select(candidate => candidate.Value));

        Assert.All(viewModel.Candidates, candidate => Assert.True(candidate.IsSelected));
        Assert.Equal("album", viewModel.Candidates[0].Source);
        Assert.Equal("フォルダ名", viewModel.Candidates[1].Source);
    }

    /// <summary>
    /// 選んだ候補だけが別名になり、日本語表記が <c>aliasesJa</c> に振り分けられることを確認する。
    /// </summary>
    [Fact]
    public void RegistersOnlySelectedCandidates()
    {
        AddWorkViewModel viewModel = Create();

        viewModel.Canonical = "Symphony No. 8";
        viewModel.Candidates[1].IsSelected = false;
        viewModel.ExtraAliasesText = "交響曲第8番";

        WorkEntry work = Assert.Single(viewModel.Apply().Works);

        Assert.Equal("Anton Bruckner", work.Composer);
        Assert.Equal(["Bruckner Symphony No.8"], work.Aliases);
        Assert.Equal(["ブルックナー 8", "交響曲第8番"], work.AliasesJa);
    }

    /// <summary>
    /// 作品名の候補を出し、押したときだけ入力欄に入ることを確認する（docs/SPEC.md 7.3.2）。
    ///
    /// 候補が無いと `Nielsen Symphony No.4` を見ながら `Symphony No. 4` を毎回手で打つことになる。
    /// **それでも既定値にはしない。** 現在の album は誤っていることがある。
    /// </summary>
    [Fact]
    public void OffersWorkNameCandidatesWithoutFillingTheField()
    {
        AddWorkViewModel viewModel = Create();

        Assert.Equal(string.Empty, viewModel.Canonical);

        WorkNameCandidate candidate = viewModel.NameCandidates[0];

        Assert.Equal("Symphony No. 8", candidate.Value);
        Assert.Equal("album", candidate.Source);

        viewModel.UseNameCandidateCommand.Execute(candidate);

        Assert.Equal("Symphony No. 8", viewModel.Canonical);
    }

    /// <summary>
    /// album とフォルダ名が別の番号を指しているときは名指しで警告する（docs/SPEC.md 7.4.3 手順5）。
    ///
    /// 実ライブラリには `シューベルト 9` のフォルダに `Schubert Symphony No.8` という album が
    /// 付いた単位がある。候補を並べるだけでは、この食い違いは見落とされる。
    /// </summary>
    [Fact]
    public void WarnsWhenAlbumAndFolderPointToDifferentNumbers()
    {
        AddWorkViewModel viewModel = Create(folder: Path.Combine("ブルックナー", "ブルックナー 9 - ショルティ"));

        Assert.Contains("album は 8 番、フォルダ名は 9 番", viewModel.Notice, StringComparison.Ordinal);
    }

    /// <summary>
    /// 既にある作品名を入れると、行を増やさずに別名だけを足すことを注意書きで伝える。
    /// </summary>
    [Fact]
    public void WarnsWhenWorkAlreadyExists()
    {
        AddWorkViewModel viewModel = Create(
        [
            new WorkEntry { Composer = "Anton Bruckner", Canonical = "Symphony No. 8" },
        ]);

        viewModel.Canonical = "Symphony No. 8";

        Assert.Contains("既に作品", viewModel.Notice, StringComparison.Ordinal);
        Assert.Single(viewModel.Apply().Works);
    }

    /// <summary>
    /// ブルックナー 8 番の単位を対象にしたダイアログを作る。
    /// </summary>
    /// <param name="works">辞書に入れておく作品。</param>
    /// <param name="folder">単位のフォルダ。album と食い違う番号を試すときに変える。</param>
    /// <returns>ビューモデル。</returns>
    private static AddWorkViewModel Create(IReadOnlyList<WorkEntry>? works = null, string? folder = null)
    {
        TagDictionary dictionary = new()
        {
            Composers =
            [
                new ComposerEntry { Canonical = "Anton Bruckner", Aliases = ["Bruckner"], AliasesJa = ["ブルックナー"] },
            ],
            Works = works ?? [],
        };

        AlbumUnit unit = new(
            folder ?? Path.Combine("ブルックナー", "ブルックナー 8 - ショルティ"),
            1,
            [],
            ["Anton Bruckner"],
            ["Georg Solti"],
            ["1990"],
            ["Bruckner Symphony No.8"]);

        return new AddWorkViewModel(dictionary, new DictionaryIndex(dictionary), unit, "Anton Bruckner");
    }
}
