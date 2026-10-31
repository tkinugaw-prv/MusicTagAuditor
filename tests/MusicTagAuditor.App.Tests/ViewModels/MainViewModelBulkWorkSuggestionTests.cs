using System.IO;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Abstractions;
using MusicTagAuditor.Core.Applying;
using MusicTagAuditor.Core.Backup;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;
using MusicTagAuditor.Core.Settings;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// 一括入力の欄に出す作品名の候補のテスト（docs/SPEC.md 5.2）。
/// 候補は選択行の作曲家で決まり、入力欄のモードは対象フィールドに追従する。
/// </summary>
public sealed class MainViewModelBulkWorkSuggestionTests : IDisposable
{
    /// <summary>テスト用の作業ディレクトリ。ライブラリ・設定・辞書をここに置く。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MusicTagAuditor-vm-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 作業ディレクトリを用意する。ファイル名の先頭を作曲家としてタグを作る。
    /// </summary>
    public MainViewModelBulkWorkSuggestionTests()
    {
        string album = Path.Combine(_root, "library", "album");
        Directory.CreateDirectory(album);

        foreach (string name in new[] { "Beethoven 1.m4a", "Beethoven 2.m4a", "Mahler 1.m4a" })
        {
            File.WriteAllBytes(Path.Combine(album, name), []);
        }
    }

    /// <summary>選択行の作曲家が 1 人なら、その作曲家の作品が出ることを確認する。</summary>
    [Fact]
    public async Task 作曲家が1人の選択なら作品名の候補が出る()
    {
        MainViewModel viewModel = await CreateViewModelAsync();
        viewModel.BulkField = TagField.Title;

        viewModel.UpdateBulkSelection(Rows(viewModel, "Beethoven"));

        Assert.True(viewModel.BulkRequiresExplicitSelection);
        Assert.Equal("Leonore Overture No. 3", Assert.Single(viewModel.BulkSuggestions).Canonical);
    }

    /// <summary>選択行の作曲家が複数なら候補を出さないことを確認する。</summary>
    [Fact]
    public async Task 作曲家が複数の選択なら候補を出さない()
    {
        MainViewModel viewModel = await CreateViewModelAsync();
        viewModel.BulkField = TagField.Title;

        viewModel.UpdateBulkSelection(Rows(viewModel, "Beethoven", "Mahler"));

        Assert.Empty(viewModel.BulkSuggestions);
    }

    /// <summary>
    /// 選択を変えずに作曲家を直しても、欄へ戻った時点の値で選び直されることを確認する。
    /// </summary>
    [Fact]
    public async Task 作曲家を直すと次に欄へ入った時点で候補が変わる()
    {
        MainViewModel viewModel = await CreateViewModelAsync();
        viewModel.BulkField = TagField.Title;

        System.Collections.IList rows = Rows(viewModel, "Mahler");
        viewModel.UpdateBulkSelection(rows);
        Assert.Equal("Symphony No. 5", Assert.Single(viewModel.BulkSuggestions).Canonical);

        ((TrackRowViewModel)rows[0]!).Composer = "Ludwig van Beethoven";
        viewModel.UpdateBulkSelection(rows);

        Assert.Equal("Leonore Overture No. 3", Assert.Single(viewModel.BulkSuggestions).Canonical);
    }

    /// <summary>
    /// 対象フィールドを切り替えると候補とモードが入れ替わることを確認する。
    /// 名前の 4 欄は明示選択にしない。
    /// </summary>
    [Fact]
    public async Task 対象フィールドの切り替えで候補とモードが入れ替わる()
    {
        MainViewModel viewModel = await CreateViewModelAsync();
        viewModel.UpdateBulkSelection(Rows(viewModel, "Beethoven"));

        viewModel.BulkField = TagField.Composer;
        Assert.False(viewModel.BulkRequiresExplicitSelection);
        Assert.Contains(viewModel.BulkSuggestions, entry => entry.Canonical == "Gustav Mahler");

        viewModel.BulkField = TagField.Album;
        Assert.False(viewModel.BulkRequiresExplicitSelection);
        Assert.Empty(viewModel.BulkSuggestions);

        viewModel.BulkField = TagField.Title;
        Assert.True(viewModel.BulkRequiresExplicitSelection);
        Assert.Equal("Leonore Overture No. 3", Assert.Single(viewModel.BulkSuggestions).Canonical);
    }

    /// <summary>
    /// 作業ディレクトリを片付ける。
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 掃除に失敗してもテストの結果は変えない。
        }
    }

    /// <summary>
    /// 指定の作曲家の行を選択に見立てて返す。
    /// </summary>
    /// <param name="viewModel">対象のビューモデル。</param>
    /// <param name="prefixes">ファイル名の先頭。</param>
    /// <returns>選択行。</returns>
    private static System.Collections.IList Rows(MainViewModel viewModel, params string[] prefixes)
    {
        return viewModel.VisibleTracks()
            .Where(row => prefixes.Any(prefix => row.FileName.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>
    /// 作品を登録した辞書でライブラリを開いたビューモデルを作る。
    /// </summary>
    /// <returns>ビューモデル。</returns>
    private async Task<MainViewModel> CreateViewModelAsync()
    {
        string settingsDirectory = Path.Combine(_root, "settings");
        Directory.CreateDirectory(settingsDirectory);

        DictionaryStore dictionaryStore = new(settingsDirectory);
        dictionaryStore.ReplaceWithoutSaving(new TagDictionary
        {
            Composers =
            [
                new ComposerEntry { Canonical = "Ludwig van Beethoven" },
                new ComposerEntry { Canonical = "Gustav Mahler" },
            ],
            Works =
            [
                new WorkEntry { Composer = "Ludwig van Beethoven", Canonical = "Leonore Overture No. 3" },
                new WorkEntry { Composer = "Gustav Mahler", Canonical = "Symphony No. 5" },
            ],
        });

        SnapshotService snapshotService = new(() => Path.Combine(_root, "backup"));
        ComposerFromFileNameTagReader tagReader = new();
        NullTagWriter tagWriter = new();

        MainViewModel viewModel = new(
            new LibraryScanner(tagReader),
            snapshotService,
            new RestoreService(tagWriter, tagReader),
            new InspectionEngine(),
            dictionaryStore,
            new DictionaryViewModel(dictionaryStore),
            new ApplyService(tagWriter, tagReader, snapshotService),
            new AppSettingsStore(settingsDirectory));

        await viewModel.OpenAsync(Path.Combine(_root, "library"));

        return viewModel;
    }

    /// <summary>
    /// 実ファイルを読まないタグリーダー。ファイル名の先頭から作曲家を決める。
    /// </summary>
    private sealed class ComposerFromFileNameTagReader : ITagReader
    {
        /// <inheritdoc />
        public TrackTags Read(string fullPath, string relativePath)
        {
            string composer = Path.GetFileName(relativePath).StartsWith("Beethoven", StringComparison.Ordinal)
                ? "Ludwig van Beethoven"
                : "Gustav Mahler";

            return new TrackTags
            {
                RelativePath = relativePath,
                FullPath = fullPath,
                Format = AudioFormat.M4a,
                Fields = TrackTags.BuildFields(
                [
                    new KeyValuePair<TagField, IReadOnlyList<string>>(TagField.Composer, [composer]),
                ]),
                RawTags = new Dictionary<string, string[]>(),
            };
        }
    }

    /// <summary>
    /// 何も書かないタグライター。このテストは書き込みまで進まない。
    /// </summary>
    private sealed class NullTagWriter : ITagWriter
    {
        /// <inheritdoc />
        public void Write(string fullPath, IReadOnlyDictionary<TagField, IReadOnlyList<string>> fields)
        {
            // 書き込みは検証対象外。
        }
    }
}
