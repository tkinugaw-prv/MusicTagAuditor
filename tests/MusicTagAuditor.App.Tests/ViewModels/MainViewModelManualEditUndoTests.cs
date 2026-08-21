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
/// 保留中の手編集を取り消す導線のテスト（docs/SPEC.md 5.2）。
///
/// 戻り道が「全件破棄」しか無いと、間違えた 1 セルを消すために残したい編集まで巻き添えにするか、
/// 元の値を思い出して打ち直すことになる。1 項目・1 行の取り消しが**対象だけに効く**ことを見る。
///
/// 絞り込みは <c>CollectionView</c> で行うためスレッド親和性がある。
/// 各テストは <see cref="DispatcherTestRunner"/> で 1 本のスレッドに固定して走らせる。
/// </summary>
public sealed class MainViewModelManualEditUndoTests : IDisposable
{
    /// <summary>テスト用の作業ディレクトリ。ライブラリ・設定・辞書をここに置く。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MusicTagAuditor-undo-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 同じフォルダに 2 ファイル用意する。取り消しが他の行へ及ばないことを見るため。
    /// </summary>
    public MainViewModelManualEditUndoTests()
    {
        CreateTrack(Path.Combine("ドヴォルザーク", "ドヴォルザーク 9 - カラヤン"), "01 Adagio - Allegro molto.m4a");
        CreateTrack(Path.Combine("ドヴォルザーク", "ドヴォルザーク 9 - カラヤン"), "02 Largo.m4a");
    }

    /// <summary>
    /// 下段の一覧から 1 項目だけ取り消せることを確認する。
    /// 同じ行の他のフィールドは残る。
    /// </summary>
    [Fact]
    public void 下段からは1項目だけ取り消せる()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateOpenedViewModelAsync();

            TrackRowViewModel row = viewModel.Tracks[0];
            row.Artist = "Herbert von Karajan";
            row.Composer = "Antonín Dvořák";

            Assert.Equal(2, viewModel.ManualEditChanges.Count);

            TagChange target = viewModel.ManualEditChanges.First(change => change.Field == TagField.Artist);

            viewModel.DiscardManualEditCommand.Execute(target);

            TagChange remaining = Assert.Single(viewModel.ManualEditChanges);

            Assert.Equal(TagField.Composer, remaining.Field);
            Assert.Equal("Antonín Dvořák", row.Composer);
            Assert.True(viewModel.HasManualEdits);

            // 取り消した項目は読み取った時点の値（ここでは未設定）へ戻る。
            Assert.Null(row.Artist);
        });
    }

    /// <summary>
    /// 行の取り消しがその行の編集だけを捨てることを確認する。
    /// </summary>
    [Fact]
    public void 行の取り消しはその行の編集だけを捨てる()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateOpenedViewModelAsync();

            TrackRowViewModel first = viewModel.Tracks[0];
            TrackRowViewModel second = viewModel.Tracks[1];

            first.Artist = "Herbert von Karajan";
            first.Album = "交響曲第 9 番";
            second.Artist = "Herbert von Karajan";

            Assert.Equal(3, viewModel.ManualEditChanges.Count);

            viewModel.ResetTrackEditsCommand.Execute(first);

            TagChange remaining = Assert.Single(viewModel.ManualEditChanges);

            Assert.Equal(second.RelativePath, remaining.RelativePath);
            Assert.False(first.IsEdited);
            Assert.True(second.IsEdited);
        });
    }

    /// <summary>
    /// 編集の無い行では取り消しを押せないことを確認する。
    /// 押しても何も起きない項目が出ていると、効かない操作を試すことになる。
    /// </summary>
    [Fact]
    public void 編集の無い行では取り消せない()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateOpenedViewModelAsync();

            TrackRowViewModel row = viewModel.Tracks[0];

            Assert.False(viewModel.ResetTrackEditsCommand.CanExecute(null));
            Assert.False(viewModel.ResetTrackEditsCommand.CanExecute(row));

            row.Artist = "Herbert von Karajan";

            Assert.True(viewModel.ResetTrackEditsCommand.CanExecute(row));
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
    /// 読み取りまで済ませたビューモデルを作る。
    /// </summary>
    /// <returns>ファイル一覧を持つビューモデル。</returns>
    private async Task<MainViewModel> CreateOpenedViewModelAsync()
    {
        MainViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(Path.Combine(_root, "library"));

        Assert.Equal(2, viewModel.Tracks.Count);

        return viewModel;
    }

    /// <summary>
    /// テスト用のビューモデルを組み立てる。
    /// </summary>
    /// <returns>ビューモデル。</returns>
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
    /// テスト用の音源ファイルを作る。中身は読まないので空でよい。
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
