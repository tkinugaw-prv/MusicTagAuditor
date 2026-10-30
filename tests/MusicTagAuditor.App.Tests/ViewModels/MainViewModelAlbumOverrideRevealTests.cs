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
/// 個別例外からファイル一覧へ移る導線のテスト（docs/SPEC.md 7.3.1）。
///
/// 選ばれる行が例外の効く範囲とずれると、その後の一括入力で別のディスクまで書き換えてしまう。
/// 飛べないときに黙ってルートへ戻ると、効いていない例外に気づけない。
/// </summary>
public sealed class MainViewModelAlbumOverrideRevealTests : IDisposable
{
    /// <summary>2 枚組のフォルダ。</summary>
    private static readonly string TWO_DISC_FOLDER = Path.Combine("ブラームス", "ブラームス 1 - ショルティ");

    /// <summary>別のフォルダ。選択に紛れ込まないことを確かめる。</summary>
    private static readonly string OTHER_FOLDER = Path.Combine("ブラームス", "ブラームス 1 - ベーム");

    /// <summary>テスト用の作業ディレクトリ。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MusicTagAuditor-overridereveal-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 2 枚組（disc 未設定を含む）と別フォルダを用意する。
    /// </summary>
    public MainViewModelAlbumOverrideRevealTests()
    {
        CreateTrack(TWO_DISC_FOLDER, "101.m4a");
        CreateTrack(TWO_DISC_FOLDER, "201.m4a");
        CreateTrack(TWO_DISC_FOLDER, "nodisc.m4a");
        CreateTrack(OTHER_FOLDER, "101.m4a");
    }

    /// <summary>
    /// 読み取り前は押せない。移っても見せる行が無い。
    /// </summary>
    [Fact]
    public void 読み取り前と未選択では押せない()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = CreateViewModel();
            AlbumOverrideRowViewModel row = Row(TWO_DISC_FOLDER);

            Assert.False(viewModel.RevealAlbumOverrideTracksCommand.CanExecute(row));

            await viewModel.OpenAsync(Path.Combine(_root, "library"));

            Assert.True(viewModel.RevealAlbumOverrideTracksCommand.CanExecute(row));
            Assert.False(viewModel.RevealAlbumOverrideTracksCommand.CanExecute(null));
        });
    }

    /// <summary>
    /// disc が空欄なら、そのフォルダの全行を選ぶ。別のフォルダは混ざらない。
    /// </summary>
    [Fact]
    public void disc_が空欄ならフォルダの全行を選ぶ()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateScannedViewModelAsync();

            IReadOnlyList<TrackRowViewModel>? revealed = Reveal(viewModel, Row(TWO_DISC_FOLDER));

            Assert.NotNull(revealed);
            Assert.Equal(3, revealed.Count);
            Assert.All(revealed, row => Assert.Equal(TWO_DISC_FOLDER, row.FolderPath));
            Assert.Equal(TWO_DISC_FOLDER, viewModel.SelectedFolder?.RelativePath);
        });
    }

    /// <summary>
    /// disc の指定があれば、そのディスクの行だけを選ぶ。
    /// </summary>
    [Fact]
    public void disc_の指定があればそのディスクの行だけを選ぶ()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateScannedViewModelAsync();

            IReadOnlyList<TrackRowViewModel>? revealed = Reveal(viewModel, Row(TWO_DISC_FOLDER, disc: 2));

            Assert.NotNull(revealed);
            Assert.Equal("201.m4a", Assert.Single(revealed).FileName);
        });
    }

    /// <summary>
    /// disc 未設定のファイルは 1 枚目として数える。検査の単位の数え方と揃える。
    /// </summary>
    [Fact]
    public void disc_未設定のファイルは1枚目に含める()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateScannedViewModelAsync();

            IReadOnlyList<TrackRowViewModel>? revealed = Reveal(viewModel, Row(TWO_DISC_FOLDER, disc: 1));

            Assert.NotNull(revealed);
            Assert.Equal(["101.m4a", "nodisc.m4a"], revealed.Select(row => row.FileName).Order(StringComparer.Ordinal));
        });
    }

    /// <summary>
    /// フォルダは検査と同じ基準で照合する。区切り文字・大文字小文字・前後の空白は違っていてよい。
    /// </summary>
    [Fact]
    public void フォルダは検査と同じ基準で照合する()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateScannedViewModelAsync();
            string written = " " + TWO_DISC_FOLDER.Replace('\\', '/') + "/ ";

            IReadOnlyList<TrackRowViewModel>? revealed = Reveal(viewModel, Row(written));

            Assert.NotNull(revealed);
            Assert.Equal(3, revealed.Count);
        });
    }

    /// <summary>
    /// フォルダが見つからなければ移動せず、ツリーも動かさず、理由を出す。
    /// </summary>
    [Fact]
    public void フォルダが見つからなければ移動しない()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateScannedViewModelAsync();
            FolderNodeViewModel? before = viewModel.SelectedFolder;

            IReadOnlyList<TrackRowViewModel>? revealed = Reveal(viewModel, Row("存在しないフォルダ"));

            Assert.Null(revealed);
            Assert.Same(before, viewModel.SelectedFolder);
            Assert.Contains("ライブラリにありません", viewModel.StatusText, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// 指定の disc が無ければ、フォルダだけを開いて行は選ばない。
    /// </summary>
    [Fact]
    public void 指定の_disc_が無ければフォルダだけを開く()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateScannedViewModelAsync();

            IReadOnlyList<TrackRowViewModel>? revealed = Reveal(viewModel, Row(TWO_DISC_FOLDER, disc: 9));

            Assert.NotNull(revealed);
            Assert.Empty(revealed);
            Assert.Equal(TWO_DISC_FOLDER, viewModel.SelectedFolder?.RelativePath);
            Assert.Contains("disc 9", viewModel.StatusText, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// 対象行を隠している絞り込みは解除し、解除したことを出す。
    /// </summary>
    [Fact]
    public void 対象行を隠す絞り込みは解除して知らせる()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateScannedViewModelAsync();
            viewModel.TrackFilterText = "ベーム";

            _ = Reveal(viewModel, Row(TWO_DISC_FOLDER));

            Assert.Equal(string.Empty, viewModel.TrackFilterText);
            Assert.Contains("検索文字列", viewModel.StatusText, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// 対象行を隠していない絞り込みは残す。組み立てた絞り込みを毎回捨てない。
    /// </summary>
    [Fact]
    public void 対象行を隠さない絞り込みは残す()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateScannedViewModelAsync();
            viewModel.TrackFilterText = "ショルティ";

            _ = Reveal(viewModel, Row(TWO_DISC_FOLDER));

            Assert.Equal("ショルティ", viewModel.TrackFilterText);
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
    /// コマンドを実行し、View へ渡された行を返す。
    /// </summary>
    /// <param name="viewModel">対象のビューモデル。</param>
    /// <param name="row">個別例外の行。</param>
    /// <returns>要求された行。要求が出なければ null。</returns>
    private static IReadOnlyList<TrackRowViewModel>? Reveal(MainViewModel viewModel, AlbumOverrideRowViewModel row)
    {
        IReadOnlyList<TrackRowViewModel>? revealed = null;
        viewModel.TrackRevealRequested += (_, rows) => revealed = rows;

        viewModel.RevealAlbumOverrideTracksCommand.Execute(row);

        return revealed;
    }

    /// <summary>
    /// 個別例外の行を作る。
    /// </summary>
    /// <param name="folder">フォルダ。</param>
    /// <param name="disc">ディスク番号。null は全ディスク。</param>
    /// <returns>編集行。</returns>
    private static AlbumOverrideRowViewModel Row(string folder, int? disc = null)
    {
        return new AlbumOverrideRowViewModel(new AlbumOverrideEntry { Folder = folder, Disc = disc });
    }

    /// <summary>
    /// テスト用の空ファイルを作る。ディスク番号はファイル名から決まる（<see cref="DiscByNameTagReader"/>）。
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
    /// スキャンまで済ませたビューモデルを作る。
    /// </summary>
    /// <returns>ファイル一覧が埋まったビューモデル。</returns>
    private async Task<MainViewModel> CreateScannedViewModelAsync()
    {
        MainViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(Path.Combine(_root, "library"));

        Assert.Equal(4, viewModel.Tracks.Count);

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
        DiscByNameTagReader tagReader = new();
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
    /// ファイル名の先頭の数字をディスク番号として返すタグリーダー。数字で始まらなければ未設定。
    /// </summary>
    private sealed class DiscByNameTagReader : ITagReader
    {
        /// <inheritdoc />
        public TrackTags Read(string fullPath, string relativePath)
        {
            char head = Path.GetFileName(relativePath)[0];
            Dictionary<TagField, IReadOnlyList<string>> fields = [];

            if (char.IsAsciiDigit(head))
            {
                fields[TagField.DiscNumber] = [head.ToString()];
            }

            return new TrackTags
            {
                RelativePath = relativePath,
                FullPath = fullPath,
                Format = AudioFormat.M4a,
                Fields = TrackTags.BuildFields(fields),
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
