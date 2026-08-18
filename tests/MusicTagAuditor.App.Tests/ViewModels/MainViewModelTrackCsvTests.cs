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
/// ファイル一覧の CSV 出力が書き出す範囲のテスト（docs/SPEC.md 5.2）。
///
/// 書き出すのは**画面に出ている行だけ**である。ここが崩れると、絞り込んで確認した
/// つもりの利用者に、隠したはずの行まで含む CSV が渡る。表と CSV で件数が違うと、
/// どちらが本当かを確かめる手立てが無い。
///
/// 絞り込みは <c>CollectionView</c> で行うためスレッド親和性がある。
/// 各テストは <see cref="DispatcherTestRunner"/> で 1 本のスレッドに固定して走らせる。
/// </summary>
public sealed class MainViewModelTrackCsvTests : IDisposable
{
    /// <summary>テスト用の作業ディレクトリ。ライブラリ・設定・辞書をここに置く。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MusicTagAuditor-trackcsv-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 作曲家の違う 2 フォルダを用意する。フォルダの絞り込みを試すため。
    /// </summary>
    public MainViewModelTrackCsvTests()
    {
        CreateTrack(Path.Combine("ブルックナー", "ブルックナー 8 - ショルティ"), "01 Allegro moderato.m4a");
        CreateTrack(Path.Combine("ブルックナー", "ブルックナー 8 - ショルティ"), "02 Scherzo.m4a");
        CreateTrack(Path.Combine("ブラームス", "ブラームス 1 - ザンデルリング"), "01 Un poco sostenuto.m4a");
    }

    /// <summary>
    /// 読み取る前は書き出せない。空の CSV を作らせても意味が無い。
    /// </summary>
    [Fact]
    public void 読み取る前は書き出せない()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = CreateViewModel();

            Assert.False(viewModel.ExportTrackCsvCommand.CanExecute(null));

            await viewModel.OpenAsync(Path.Combine(_root, "library"));

            Assert.True(viewModel.ExportTrackCsvCommand.CanExecute(null));
        });
    }

    /// <summary>
    /// 絞り込んでいなければ全件が対象になる。
    /// </summary>
    [Fact]
    public void 既定では読み取った全件が対象になる()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateScannedViewModelAsync();

            Assert.Equal(3, viewModel.VisibleTracks().Count);
        });
    }

    /// <summary>
    /// 検索文字列で絞ると、書き出す範囲も同じだけ狭まる。
    /// </summary>
    [Fact]
    public void 検索文字列で絞った分だけ対象が減る()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateScannedViewModelAsync();

            viewModel.TrackFilterText = "ブラームス";

            Assert.Single(viewModel.VisibleTracks());
            Assert.All(
                viewModel.VisibleTracks(),
                row => Assert.Contains("ブラームス", row.RelativePath, StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// 「編集した行のみ」で絞った状態も、そのまま書き出す範囲になる。
    /// </summary>
    [Fact]
    public void 編集した行のみに絞った分だけ対象が減る()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateScannedViewModelAsync();

            TrackRowViewModel edited = viewModel.Tracks[0];
            edited.Composer = "Anton Bruckner";

            viewModel.ShowOnlyEditedTracks = true;

            Assert.Equal([edited], viewModel.VisibleTracks());
        });
    }

    /// <summary>
    /// 左ツリーでフォルダを選んだら、その配下だけが対象になる。
    /// </summary>
    [Fact]
    public void フォルダを選ぶと配下だけが対象になる()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateScannedViewModelAsync();

            viewModel.SelectedFolder = viewModel.FolderTree[0].Children
                .First(node => node.Name == "ブルックナー");

            Assert.Equal(2, viewModel.VisibleTracks().Count);
            Assert.All(
                viewModel.VisibleTracks(),
                row => Assert.StartsWith("ブルックナー", row.RelativePath, StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// 絞り込みを外せば、隠れていた行がそのまま戻る。
    /// </summary>
    [Fact]
    public void 絞り込みを外すと対象が戻る()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateScannedViewModelAsync();

            viewModel.TrackFilterText = "ブラームス";
            viewModel.TrackFilterText = string.Empty;

            Assert.Equal(3, viewModel.VisibleTracks().Count);
        });
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
    /// テスト用の空ファイルを作る。
    /// </summary>
    /// <param name="relativeDirectory">ライブラリルートからのフォルダ。</param>
    /// <param name="fileName">ファイル名。</param>
    private void CreateTrack(string relativeDirectory, string fileName)
    {
        string directory = Path.Combine(_root, "library", relativeDirectory);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, fileName), []);
    }

    /// <summary>
    /// スキャンまで済ませたビューモデルを作る。検査は要らない。
    /// </summary>
    /// <returns>ファイル一覧が埋まったビューモデル。</returns>
    private async Task<MainViewModel> CreateScannedViewModelAsync()
    {
        MainViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(Path.Combine(_root, "library"));

        Assert.Equal(3, viewModel.Tracks.Count);

        return viewModel;
    }

    /// <summary>
    /// ビューモデルを組み立てる。
    /// </summary>
    /// <returns>まだ何も開いていないビューモデル。</returns>
    private MainViewModel CreateViewModel()
    {
        string settingsDirectory = Path.Combine(_root, "settings");
        Directory.CreateDirectory(settingsDirectory);

        DictionaryStore dictionaryStore = new(settingsDirectory);
        SnapshotService snapshotService = new(() => Path.Combine(_root, "backup"));
        EmptyTagReader tagReader = new();
        NullTagWriter tagWriter = new();

        return new MainViewModel(
            new LibraryScanner(tagReader),
            snapshotService,
            new RestoreService(tagWriter, tagReader),
            new InspectionEngine(),
            dictionaryStore,
            new DictionaryViewModel(dictionaryStore),
            new ApplyService(tagWriter, tagReader, snapshotService),
            new AppSettingsStore(settingsDirectory));
    }

    /// <summary>
    /// 実ファイルを読まないタグリーダー。タグはすべて未設定として返す。
    /// </summary>
    private sealed class EmptyTagReader : ITagReader
    {
        /// <inheritdoc />
        public TrackTags Read(string fullPath, string relativePath)
        {
            return new TrackTags
            {
                RelativePath = relativePath,
                FullPath = fullPath,
                Format = AudioFormat.M4a,
                Fields = TrackTags.BuildFields([]),
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
