using System.ComponentModel;
using System.IO;
using System.Windows.Data;
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
/// 検査結果タブ下段の「チェック済みのみ」表示のテスト（docs/SPEC.md 5.3）。
///
/// これは**表示だけ**の絞り込みである。隠すのは適用されない行だけなので、
/// 上段の件数・適用対象・チェック状態はどれも動いてはいけない。
/// ここが崩れると、画面から消しただけの行が適用対象から外れる（またはその逆）。
///
/// 絞り込みは <c>CollectionView</c> で行う。スレッド親和性があるため、
/// 各テストは <see cref="DispatcherTestRunner"/> で 1 本のスレッドに固定して走らせる。
/// </summary>
public sealed class MainViewModelCheckedOnlyFilterTests : IDisposable
{
    /// <summary>テスト用の作業ディレクトリ。ライブラリ・設定・辞書をここに置く。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MusicTagAuditor-checked-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 作業ディレクトリを用意する。
    /// </summary>
    public MainViewModelCheckedOnlyFilterTests()
    {
        CreateTrack("01 Allegro moderato.m4a");
        CreateTrack("02 Scherzo.m4a");
        CreateTrack("03 Adagio.m4a");
    }

    /// <summary>
    /// 既定では絞り込まない。検査直後は既定のチェックを見直すところから始まるので、
    /// 未チェックの行が最初から隠れていては選びようがない。
    /// </summary>
    [Fact]
    public void 既定ではチェックの有無にかかわらず全件出る()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateInspectedViewModelAsync();
            TagChangeViewModel target = SelectRuleWithFixableChanges(viewModel);

            target.IsSelected = false;

            Assert.False(viewModel.ShowOnlySelectedChanges);
            Assert.Equal(viewModel.InspectionChanges.Count, VisibleChanges(viewModel).Count);
            Assert.Contains(target, VisibleChanges(viewModel));
        });
    }

    /// <summary>
    /// 有効にすると未チェックの行が消え、残るのはチェック済みだけになる。
    /// </summary>
    [Fact]
    public void 有効にするとチェック済みの行だけが残る()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateInspectedViewModelAsync();
            TagChangeViewModel target = SelectRuleWithFixableChanges(viewModel);

            target.IsSelected = false;

            viewModel.ShowOnlySelectedChanges = true;

            IReadOnlyList<TagChangeViewModel> visible = VisibleChanges(viewModel);

            Assert.NotEmpty(visible);
            Assert.All(visible, change => Assert.True(change.IsSelected));
            Assert.DoesNotContain(target, visible);
        });
    }

    /// <summary>
    /// 絞り込み中にチェックを外した行は、その場で一覧から消える。
    ///
    /// 消えるのは表示だけで、明細そのものは残る。
    /// </summary>
    [Fact]
    public void 絞り込み中にチェックを外した行は消える()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateInspectedViewModelAsync();
            TagChangeViewModel target = SelectRuleWithFixableChanges(viewModel);

            viewModel.ShowOnlySelectedChanges = true;

            int before = VisibleChanges(viewModel).Count;

            target.IsSelected = false;

            Assert.Equal(before - 1, VisibleChanges(viewModel).Count);
            Assert.DoesNotContain(target, VisibleChanges(viewModel));

            // 母集合は減っていない。
            Assert.Contains(target, viewModel.InspectionChanges);
        });
    }

    /// <summary>
    /// 絞り込みを外すと、隠れていた行がそのまま戻る。
    /// </summary>
    [Fact]
    public void 絞り込みを外すと隠れていた行が戻る()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateInspectedViewModelAsync();
            TagChangeViewModel target = SelectRuleWithFixableChanges(viewModel);

            target.IsSelected = false;

            int total = viewModel.InspectionChanges.Count;

            viewModel.ShowOnlySelectedChanges = true;
            viewModel.ShowOnlySelectedChanges = false;

            Assert.Equal(total, VisibleChanges(viewModel).Count);
            Assert.Contains(target, VisibleChanges(viewModel));
            Assert.False(target.IsSelected);
        });
    }

    /// <summary>
    /// 表示だけの絞り込みであり、**件数・適用対象・チェック状態は動かさない**。
    ///
    /// フォルダの絞り込みと違って対象範囲そのものは狭めない。ここが崩れると、
    /// 見た目を整えたつもりの操作が書き込む内容を変える。
    /// </summary>
    [Fact]
    public void 件数と適用対象は動かさない()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateInspectedViewModelAsync();
            TagChangeViewModel target = SelectRuleWithFixableChanges(viewModel);

            target.IsSelected = false;

            List<int> counts = [.. viewModel.RuleResults.Select(rule => rule.Count)];
            string summary = viewModel.InspectionSummary;
            bool canApply = viewModel.CanApplyChanges;

            viewModel.ShowOnlySelectedChanges = true;

            Assert.Equal(counts, viewModel.RuleResults.Select(rule => rule.Count));
            Assert.Equal(summary, viewModel.InspectionSummary);
            Assert.Equal(canApply, viewModel.CanApplyChanges);

            // 隠れている行のチェック状態も触らない。
            Assert.False(target.IsSelected);
            Assert.False(target.Change.IsSelected);
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
    /// 下段の一覧に実際に出ている明細を取り出す。
    /// </summary>
    /// <param name="viewModel">対象のビューモデル。</param>
    /// <returns>表示されている明細。</returns>
    private static IReadOnlyList<TagChangeViewModel> VisibleChanges(MainViewModel viewModel)
    {
        ICollectionView view = CollectionViewSource.GetDefaultView(viewModel.InspectionChanges);

        return [.. view.Cast<TagChangeViewModel>()];
    }

    /// <summary>
    /// 修正案を複数持つルールを選び、そのうち 1 件を返す。
    /// </summary>
    /// <param name="viewModel">対象のビューモデル。</param>
    /// <returns>チェックを外して試す明細。</returns>
    private static TagChangeViewModel SelectRuleWithFixableChanges(MainViewModel viewModel)
    {
        viewModel.SelectedRule = viewModel.RuleResults.First(rule => rule.FixableCount >= 2);

        TagChangeViewModel target = viewModel.InspectionChanges.First(change => change.IsSelected);

        Assert.True(viewModel.InspectionChanges.Count(change => change.IsSelected) >= 2);

        return target;
    }

    /// <summary>
    /// テスト用の空ファイルを作る。
    /// </summary>
    /// <param name="fileName">ファイル名。</param>
    private void CreateTrack(string fileName)
    {
        string directory = Path.Combine(_root, "library", "ブルックナー", "ブルックナー 8 - ショルティ");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, fileName), []);
    }

    /// <summary>
    /// 検査まで済ませたビューモデルを作る。
    /// </summary>
    /// <returns>検査結果を持つビューモデル。</returns>
    private async Task<MainViewModel> CreateInspectedViewModelAsync()
    {
        string settingsDirectory = Path.Combine(_root, "settings");
        Directory.CreateDirectory(settingsDirectory);

        DictionaryStore dictionaryStore = new(settingsDirectory);
        SnapshotService snapshotService = new(() => Path.Combine(_root, "backup"));
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

        viewModel.InspectCommand.Execute(null);

        Assert.NotEmpty(viewModel.RuleResults);

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
