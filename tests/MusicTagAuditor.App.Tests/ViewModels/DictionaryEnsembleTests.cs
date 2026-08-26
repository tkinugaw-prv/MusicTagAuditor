using System.IO;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// 辞書タブの団体の設定（<c>noConductor</c>）のテスト。
///
/// **保存は編集行から辞書を組み立て直す。** 組み立てから漏れた項目は、その団体を触っていなくても
/// 保存のたびに既定値へ戻る。実際に <c>noConductor</c> が漏れており、辞書タブで別の行を直して
/// 保存しただけで I Musici / Smetana Quartet の設定が落ち、R-402 が誤検出に戻っていた
/// （docs/TAGGING_POLICY.md 2.2）。**読めて、直せて、保存で失われない**の 3 点をここで守る。
/// </summary>
public sealed class DictionaryEnsembleTests : IDisposable
{
    /// <summary>テスト用の作業ディレクトリ。辞書をここに置く。</summary>
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MusicTagAuditor-dict-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 作業ディレクトリを用意する。
    /// </summary>
    public DictionaryEnsembleTests()
    {
        Directory.CreateDirectory(_directory);
    }

    /// <summary>
    /// 辞書の <c>noConductor</c> が編集行に読み込まれることを確認する。
    /// 画面に出ていなければ、立っているかどうかを利用者が確かめられない。
    /// </summary>
    [Fact]
    public void LoadsNoConductor()
    {
        DictionaryViewModel viewModel = new(CreateStore());

        Assert.True(Find(viewModel, "it-i-musici").NoConductor);
        Assert.False(Find(viewModel, "de-berliner-phil").NoConductor);
    }

    /// <summary>
    /// **別の団体を編集して保存しても** <c>noConductor</c> が保たれることを確認する。
    ///
    /// これが今回の回帰そのものである。触っていない団体の設定が道連れで落ちていた。
    /// </summary>
    [Fact]
    public void KeepsNoConductorWhenAnotherRowIsEdited()
    {
        DictionaryStore store = CreateStore();
        DictionaryViewModel viewModel = new(store);

        Find(viewModel, "de-berliner-phil").AliasesJaText = "ベルリン・フィル";
        viewModel.SaveCommand.Execute(null);

        Assert.False(viewModel.IsDirty);
        Assert.True(Entry(store, "it-i-musici").NoConductor);
    }

    /// <summary>
    /// 画面で立てた <c>noConductor</c> が保存されることを確認する。
    ///
    /// 既定辞書に無い団体は、この導線でしか指揮者不在を宣言できない。
    /// </summary>
    [Fact]
    public void SavesNoConductorSetInTab()
    {
        DictionaryStore store = CreateStore();
        DictionaryViewModel viewModel = new(store);

        Find(viewModel, "cz-smetana-quartet").NoConductor = true;
        viewModel.SaveCommand.Execute(null);

        Assert.False(viewModel.IsDirty);
        Assert.True(Entry(store, "cz-smetana-quartet").NoConductor);

        // 下ろした状態も保存できること。立てるだけでは判断を取り消せない。
        Find(viewModel, "it-i-musici").NoConductor = false;
        viewModel.SaveCommand.Execute(null);

        Assert.False(Entry(store, "it-i-musici").NoConductor);
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
    /// 実体 ID から編集行を取り出す。
    /// </summary>
    /// <param name="viewModel">対象のビューモデル。</param>
    /// <param name="entityId">実体 ID。</param>
    /// <returns>編集行。</returns>
    private static EnsembleRowViewModel Find(DictionaryViewModel viewModel, string entityId)
    {
        return viewModel.Ensembles.Single(row => row.EntityId == entityId);
    }

    /// <summary>
    /// 実体 ID から保存後のエントリを取り出す。
    /// </summary>
    /// <param name="store">対象の辞書ストア。</param>
    /// <param name="entityId">実体 ID。</param>
    /// <returns>エントリ。</returns>
    private static EnsembleEntry Entry(DictionaryStore store, string entityId)
    {
        return (store.Dictionary.Ensembles ?? []).Single(entry => entry.EntityId == entityId);
    }

    /// <summary>
    /// 指揮者を置かない団体と置く団体を並べた辞書ストアを作る。
    /// </summary>
    /// <returns>辞書ストア。</returns>
    private DictionaryStore CreateStore()
    {
        DictionaryStore store = new(_directory);

        store.Save(store.Dictionary with
        {
            Composers = [],
            Persons = [],
            Ensembles =
            [
                new EnsembleEntry
                {
                    EntityId = "it-i-musici",
                    Canonical = "I Musici",
                    NoConductor = true,
                    AliasesJa = ["イ・ムジチ合奏団"],
                },
                new EnsembleEntry
                {
                    EntityId = "cz-smetana-quartet",
                    Canonical = "Smetana Quartet",
                },
                new EnsembleEntry
                {
                    EntityId = "de-berliner-phil",
                    Canonical = "Berliner Philharmoniker",
                },
            ],
            Works = [],
            AlbumOverrides = [],
        });

        return store;
    }
}
