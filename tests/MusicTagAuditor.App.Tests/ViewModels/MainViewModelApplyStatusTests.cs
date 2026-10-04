using System.IO;
using System.Text.Json.Nodes;
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
/// 手編集の適用と復元が終わったあとの後始末のテスト。
///
/// どちらも完了後にライブラリを読み直す。読み直しは保留中の手編集を捨て、
/// ステータスを件数の文言で上書きするため、以前は次の 2 つが起きていた。
/// <list type="bullet">
/// <item>手編集の適用が書き込み前に失敗しても、入力した編集が黙って消える</item>
/// <item>復元の読み戻し不一致の知らせが、読み直しの文言で消えてログにしか残らない</item>
/// </list>
///
/// 絞り込みは <c>CollectionView</c> で行うためスレッド親和性がある。
/// 各テストは <see cref="DispatcherTestRunner"/> で 1 本のスレッドに固定して走らせる。
/// </summary>
public sealed class MainViewModelApplyStatusTests : IDisposable
{
    /// <summary>テスト用の作業ディレクトリ。ライブラリ・設定・辞書をここに置く。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MusicTagAuditor-status-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// バックアップの保存先。テストごとに書けない場所へ差し替える。
    /// <see cref="SnapshotService"/> は毎回これを呼び直す。
    /// </summary>
    private string _backupRoot;

    /// <summary>
    /// 同じフォルダに 2 ファイル用意する。
    /// </summary>
    public MainViewModelApplyStatusTests()
    {
        _backupRoot = Path.Combine(_root, "backup");

        string directory = Path.Combine(_root, "library", "ドヴォルザーク", "ドヴォルザーク 9 - カラヤン");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "01 Adagio - Allegro molto.m4a"), []);
        File.WriteAllBytes(Path.Combine(directory, "02 Largo.m4a"), []);
    }

    /// <summary>
    /// スナップショットを取れずに適用が失敗したとき、保留中の編集が残り、
    /// 失敗の文言が画面に残ることを確認する。
    /// </summary>
    [Fact]
    public void 適用が書き込み前に失敗したら保留中の編集を残す()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateOpenedViewModelAsync();

            TrackRowViewModel row = viewModel.Tracks[0];
            row.Artist = "Herbert von Karajan";

            // ファイルの下にフォルダは作れない。保存先のドライブが外れたのと同じく、スナップショットで失敗する。
            string blocker = Path.Combine(_root, "blocker");
            File.WriteAllBytes(blocker, []);
            _backupRoot = Path.Combine(blocker, "backup");

            await viewModel.ApplyConfirmedManualEditsAsync([.. viewModel.ManualEditChanges]);

            Assert.True(viewModel.HasManualEdits);
            Assert.Single(viewModel.ManualEditChanges);
            Assert.Equal("Herbert von Karajan", viewModel.Tracks[0].Artist);
            Assert.Contains("手編集の適用に失敗しました", viewModel.StatusText, StringComparison.Ordinal);
            Assert.False(viewModel.IsScanning);
        });
    }

    /// <summary>
    /// 復元の読み戻し不一致の知らせが、直後の読み直しで消えないことを確認する。
    /// </summary>
    [Fact]
    public void 復元の不一致は読み直し後も表示に残る()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateOpenedViewModelAsync();

            viewModel.CreateBackupCommand.Execute(null);
            BackupEntryViewModel backup = Assert.Single(viewModel.Backups);

            // スナップショットに現在と違う値を書き足して、復元項目を作る。
            // 書き込みは何もしないので、読み戻すと必ず不一致になる。
            AddGenreToFirstTrack(Path.Combine(backup.DirectoryPath, BackupConst.SNAPSHOT_FILE_NAME));

            viewModel.SelectedBackup = backup;
            await viewModel.PreviewRestoreCommand.ExecuteAsync(null);

            Assert.True(viewModel.IsRestoreReady);

            await viewModel.RestoreCommand.ExecuteAsync(null);

            Assert.Contains("読み戻して一致しなかった項目が 1 件", viewModel.StatusText, StringComparison.Ordinal);

            // 読み直しの結果も捨てない。
            Assert.Contains("件を読み取りました", viewModel.StatusText, StringComparison.Ordinal);
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
    /// スナップショットの先頭のファイルに genre を足す。
    /// </summary>
    /// <param name="snapshotPath">スナップショットのパス。</param>
    private static void AddGenreToFirstTrack(string snapshotPath)
    {
        JsonNode snapshot = JsonNode.Parse(File.ReadAllText(snapshotPath))!;
        JsonNode track = snapshot["tracks"]![0]!;
        track["fields"]![nameof(TagField.Genre)] = new JsonArray("Classic");
        File.WriteAllText(snapshotPath, snapshot.ToJsonString());
    }

    /// <summary>
    /// ライブラリを開いた状態のビューモデルを作る。
    /// </summary>
    /// <returns>ビューモデル。</returns>
    private async Task<MainViewModel> CreateOpenedViewModelAsync()
    {
        string settingsDirectory = Path.Combine(_root, "settings");
        Directory.CreateDirectory(settingsDirectory);

        DictionaryStore dictionaryStore = new(settingsDirectory);
        SnapshotService snapshotService = new(() => _backupRoot);
        EmptyTagReader tagReader = new();
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

        Assert.Equal(2, viewModel.Tracks.Count);

        return viewModel;
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
    /// 何も書かないタグライター。復元の読み戻しを必ず不一致にするために使う。
    /// </summary>
    private sealed class NullTagWriter : ITagWriter
    {
        /// <inheritdoc />
        public void Write(string fullPath, IReadOnlyDictionary<TagField, IReadOnlyList<string>> fields)
        {
            // 書き込まない。
        }
    }
}
