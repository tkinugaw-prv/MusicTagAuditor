using MusicTagger.Core.Dictionary;

namespace MusicTagger.Core.Tests.Dictionary;

/// <summary>
/// 辞書の保存・読み直しのテスト。
/// 辞書は利用者が育てる資産なので、往復で内容が落ちないことを確認する。
/// </summary>
public sealed class DictionaryStoreTests : IDisposable
{
    /// <summary>テスト用の作業フォルダ。</summary>
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"musicTagger.tests.{Guid.NewGuid():N}");

    /// <summary>
    /// 保存した内容が読み直しで戻ることを確認する。
    /// </summary>
    [Fact]
    public void SavedDictionaryRoundTrips()
    {
        DictionaryStore store = new(_directory);

        TagDictionary edited = DictionaryEditor.AddEnsemble(
            store.Dictionary, "jp-nhk-so", "NHK Symphony Orchestra", ["NHK交響楽団"]);

        store.Save(edited);
        store.Reload();

        Assert.True(store.Index.TryResolveEnsemble("NHK交響楽団", out EnsembleEntry ensemble));
        Assert.Equal("jp-nhk-so", ensemble.EntityId);
    }

    /// <summary>
    /// 保存すると索引が作り直されることを確認する。
    /// 索引が古いままだと、追加した値がその場では効かない。
    /// </summary>
    [Fact]
    public void RebuildsIndexOnSave()
    {
        DictionaryStore store = new(_directory);

        Assert.False(store.Index.TryResolvePerson("テスト指揮者", out _));

        store.Save(DictionaryEditor.AddPerson(
            store.Dictionary, "Test Conductor", [PersonRole.Conductor], ["テスト指揮者"]));

        Assert.True(store.Index.TryResolvePerson("テスト指揮者", out PersonEntry person));
        Assert.Equal("Test Conductor", person.Canonical);
    }

    /// <summary>
    /// 上書き保存で直前版が <c>.bak</c> に残ることを確認する。
    /// 誤った一括編集を手で戻せるようにするため。
    /// </summary>
    [Fact]
    public void KeepsPreviousVersionAsBackup()
    {
        DictionaryStore store = new(_directory);

        store.Save(store.Dictionary);
        store.Save(DictionaryEditor.AddComposer(store.Dictionary, "Test Composer"));

        Assert.True(File.Exists(store.FilePath + DictionaryWriter.BACKUP_SUFFIX));
    }

    /// <summary>
    /// 同梱辞書の注意書き（<c>_comment</c> 等）が書き出しで失われないことを確認する。
    ///
    /// これを落とすと「推測で名前を足さないこと」といった前提が辞書から消える。
    /// </summary>
    [Fact]
    public void PreservesCommentProperties()
    {
        DictionaryStore store = new(_directory);

        store.Save(store.Dictionary);

        string json = File.ReadAllText(store.FilePath);

        Assert.Contains("_comment", json, StringComparison.Ordinal);
        Assert.Contains("推測で名前を足さないこと", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// 書き出した JSON の日本語がエスケープされずそのまま読めることを確認する。
    /// 辞書は人が手で開いて確認するファイルなので、<c>\uXXXX</c> になっては困る。
    /// </summary>
    [Fact]
    public void WritesReadableJapanese()
    {
        DictionaryStore store = new(_directory);

        store.Save(DictionaryEditor.AddPerson(
            store.Dictionary, "Test Conductor", [PersonRole.Conductor], ["テスト指揮者"]));

        string json = File.ReadAllText(store.FilePath);

        Assert.Contains("テスト指揮者", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u30C6", json, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ウムラウトが正しく往復することを確認する。
    /// <c>Böhm</c> が壊れると照合そのものが崩れる。
    /// </summary>
    [Fact]
    public void PreservesDiacritics()
    {
        DictionaryStore store = new(_directory);

        store.Save(store.Dictionary);
        store.Reload();

        Assert.True(store.Index.TryResolvePerson("Karl Böhm", out PersonEntry person));
        Assert.Equal("Karl Böhm", person.Canonical);
    }

    /// <summary>
    /// 任意のパスへ書き出せることを確認する。同梱の既定辞書へ書き戻す導線で使う。
    /// </summary>
    [Fact]
    public void ExportsToArbitraryPath()
    {
        DictionaryStore store = new(_directory);
        string path = Path.Combine(_directory, "export", "default-dictionary.json");

        store.Export(path);

        Assert.True(File.Exists(path));
        Assert.Contains("Karl Böhm", File.ReadAllText(path), StringComparison.Ordinal);
    }

    /// <summary>
    /// 作業フォルダを片付ける。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
