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
/// 検査結果タブをツリーの選択フォルダで絞り込む挙動のテスト（docs/SPEC.md 5.3）。
///
/// 絞り込みは表示だけでなく、上段の件数・一括選択・適用対象・CSV 出力まで同じ範囲に揃える。
/// **画面に出ていない項目が書き込まれる状態を作らない**ことが肝なので、
/// 範囲外の <see cref="TagChange.IsSelected"/> が動かないことを重点的に見る。
///
/// フォルダを選び直すとファイル一覧の <c>CollectionView</c> が動く。スレッド親和性があるため、
/// 各テストは <see cref="DispatcherTestRunner"/> で 1 本のスレッドに固定して走らせる。
/// </summary>
public sealed class MainViewModelInspectionScopeTests : IDisposable
{
    /// <summary>テスト用の作業ディレクトリ。ライブラリ・設定・辞書をここに置く。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MusicTagAuditor-scope-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 作業ディレクトリを用意する。絞り込みを見るため作曲家フォルダを 2 つ作る。
    /// </summary>
    public MainViewModelInspectionScopeTests()
    {
        CreateTrack("ブルックナー", "ブルックナー 8 - ショルティ", "01 Allegro moderato.m4a");
        CreateTrack("ブルックナー", "ブルックナー 8 - ショルティ", "02 Scherzo.m4a");
        CreateTrack("バッハ", "ゴルトベルク変奏曲 - グールド", "01 Aria.m4a");
    }

    /// <summary>
    /// 既定では絞り込みが効かず、フォルダを選んでも検出件数が変わらないことを確認する。
    ///
    /// ツリーはファイル一覧タブの操作にも使う。フォルダを選んだだけで検査結果が狭まると、
    /// 全体を見ているつもりの利用者を欺く。
    /// </summary>
    [Fact]
    public void 既定ではフォルダを選んでも検査結果は絞られない()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateInspectedViewModelAsync();

            int before = viewModel.RuleResults.Sum(rule => rule.Count);

            viewModel.SelectedFolder = LocateFolder(viewModel, "バッハ");

            Assert.False(viewModel.LimitInspectionToSelectedFolder);
            Assert.Equal(before, viewModel.RuleResults.Sum(rule => rule.Count));
        });
    }

    /// <summary>
    /// 絞り込みを有効にすると、上段の件数が選択フォルダ配下だけになることを確認する。
    /// </summary>
    [Fact]
    public void 絞り込みを有効にすると上段の件数が配下だけになる()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateInspectedViewModelAsync();

            int total = viewModel.RuleResults.Sum(rule => rule.Count);

            viewModel.SelectedFolder = LocateFolder(viewModel, "バッハ");
            viewModel.LimitInspectionToSelectedFolder = true;

            int scoped = viewModel.RuleResults.Sum(rule => rule.Count);

            Assert.True(scoped > 0, "バッハ配下にも検出があるはず");
            Assert.True(scoped < total, "全体より少ないはず");

            // 表に出ているルール行は、すべて配下の明細だけを数えている。
            Assert.All(
                viewModel.RuleResults,
                rule => Assert.All(
                    rule.ScopedChanges,
                    change => Assert.StartsWith("バッハ", change.RelativePath, StringComparison.Ordinal)));

            Assert.All(viewModel.RuleResults, rule => Assert.Equal(rule.ScopedChanges.Count, rule.Count));
            Assert.All(
                viewModel.RuleResults,
                rule => Assert.Equal(rule.ScopedChanges.Count(change => change.HasFix), rule.FixableCount));
        });
    }

    /// <summary>
    /// 配下に検出が無いルールは、行ごと一覧から消えることを確認する。
    ///
    /// 0 件の行を残しても情報量が無く、検査直後に 0 件のルールを出さないのと基準が揃わない。
    /// </summary>
    [Fact]
    public void 配下に検出が無いルール行は消える()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateInspectedViewModelAsync();

            viewModel.SelectedFolder = LocateFolder(viewModel, "バッハ");
            viewModel.LimitInspectionToSelectedFolder = true;

            Assert.DoesNotContain(viewModel.RuleResults, rule => rule.Count == 0);
        });
    }

    /// <summary>
    /// 下段の差分明細も選択フォルダ配下だけになることを確認する。
    /// </summary>
    [Fact]
    public void 下段の明細も配下だけになる()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateInspectedViewModelAsync();

            viewModel.SelectedFolder = LocateFolder(viewModel, "バッハ");
            viewModel.LimitInspectionToSelectedFolder = true;

            Assert.NotEmpty(viewModel.InspectionChanges);
            Assert.All(
                viewModel.InspectionChanges,
                change => Assert.StartsWith("バッハ", change.RelativePath, StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// 一括選択が範囲内だけに効き、**範囲外のチェック状態を書き換えない**ことを確認する。
    ///
    /// ここが崩れると、1 フォルダを見ているつもりの操作がライブラリ全体のチェックを動かす。
    /// </summary>
    [Fact]
    public void 全選択は範囲外のチェックを動かさない()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateInspectedViewModelAsync();

            viewModel.DeselectAllRuleChangesCommand.Execute(null);

            viewModel.SelectedFolder = LocateFolder(viewModel, "バッハ");
            viewModel.LimitInspectionToSelectedFolder = true;

            viewModel.SelectAllRuleChangesCommand.Execute(null);

            // 範囲内は全部チェックされ、範囲外は外れたまま。
            viewModel.LimitInspectionToSelectedFolder = false;

            TagChangeViewModel[] fixable =
            [
                .. viewModel.RuleResults.SelectMany(rule => rule.Changes).Where(change => change.HasFix),
            ];

            Assert.All(
                fixable.Where(change => change.RelativePath.StartsWith("バッハ", StringComparison.Ordinal)),
                change => Assert.True(change.IsSelected));

            Assert.All(
                fixable.Where(change => !change.RelativePath.StartsWith("バッハ", StringComparison.Ordinal)),
                change => Assert.False(change.IsSelected));
        });
    }

    /// <summary>
    /// 絞り込みを外すと、件数もルール行も明細も元どおりに戻ることを確認する。
    /// </summary>
    [Fact]
    public void 絞り込みを外すと全体に戻る()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateInspectedViewModelAsync();

            string[] ruleIdsBefore = [.. viewModel.RuleResults.Select(rule => rule.RuleId)];
            int total = viewModel.RuleResults.Sum(rule => rule.Count);

            viewModel.SelectedFolder = LocateFolder(viewModel, "バッハ");
            viewModel.LimitInspectionToSelectedFolder = true;
            viewModel.LimitInspectionToSelectedFolder = false;

            string[] ruleIdsAfter = [.. viewModel.RuleResults.Select(rule => rule.RuleId)];

            Assert.Equal(ruleIdsBefore, ruleIdsAfter);
            Assert.Equal(total, viewModel.RuleResults.Sum(rule => rule.Count));
        });
    }

    /// <summary>
    /// ツリーのルートを選んでいるときは、絞り込みが有効でも全件が対象になることを確認する。
    /// <c>ApplyFolderFilter</c>（ファイル一覧タブ）と基準を揃える。
    /// </summary>
    [Fact]
    public void ルート選択なら絞り込みを有効にしても全件のまま()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateInspectedViewModelAsync();

            int total = viewModel.RuleResults.Sum(rule => rule.Count);

            viewModel.SelectedFolder = viewModel.FolderTree.First();
            viewModel.LimitInspectionToSelectedFolder = true;

            Assert.Equal(total, viewModel.RuleResults.Sum(rule => rule.Count));
        });
    }

    /// <summary>
    /// 絞り込み中は、要約テキストの件数も対象フォルダの表示も範囲に合わせることを確認する。
    /// 件数だけ減っていると、検査し直したのか絞ったのかが読めない。
    /// </summary>
    [Fact]
    public void 要約テキストが範囲と対象フォルダを示す()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateInspectedViewModelAsync();

            viewModel.SelectedFolder = LocateFolder(viewModel, "バッハ");
            viewModel.LimitInspectionToSelectedFolder = true;

            int scoped = viewModel.RuleResults.Sum(rule => rule.Count);

            Assert.Contains($"検出 {scoped:N0} 件", viewModel.InspectionSummary, StringComparison.Ordinal);
            Assert.Contains("（バッハ 配下）", viewModel.InspectionSummary, StringComparison.Ordinal);

            viewModel.LimitInspectionToSelectedFolder = false;

            Assert.DoesNotContain("配下）", viewModel.InspectionSummary, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// 適用に成功した項目を取り除いたあとも、絞り込みが保たれることを確認する。
    ///
    /// 触れたルール行は新しいインスタンスに差し替わるため、そこにも範囲を当て直す必要がある。
    /// </summary>
    [Fact]
    public void 適用後の刈り込みでも絞り込みが保たれる()
    {
        DispatcherTestRunner.Run(async () =>
        {
            MainViewModel viewModel = await CreateInspectedViewModelAsync();

            viewModel.SelectedFolder = LocateFolder(viewModel, "バッハ");
            viewModel.LimitInspectionToSelectedFolder = true;

            TagChangeViewModel target = viewModel.RuleResults
                .SelectMany(rule => rule.ScopedChanges)
                .First(change => change.HasFix);

            viewModel.RemoveSucceededFromInspection(new HashSet<TagChangeKey> { TagChangeKey.From(target.Change) });

            Assert.All(
                viewModel.RuleResults,
                rule => Assert.All(
                    rule.ScopedChanges,
                    change => Assert.StartsWith("バッハ", change.RelativePath, StringComparison.Ordinal)));

            Assert.DoesNotContain(viewModel.RuleResults, rule => rule.Count == 0);

            // 取り除いた項目は範囲内から消えている。
            Assert.DoesNotContain(viewModel.RuleResults.SelectMany(rule => rule.ScopedChanges), change => change == target);

            // 範囲外のルール行と明細は刈り込まれずに残っている。
            viewModel.LimitInspectionToSelectedFolder = false;

            Assert.Contains(
                viewModel.RuleResults.SelectMany(rule => rule.Changes),
                change => !change.RelativePath.StartsWith("バッハ", StringComparison.Ordinal));
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
    /// ツリーから名前でフォルダノードを探す。
    /// </summary>
    /// <param name="viewModel">対象のビューモデル。</param>
    /// <param name="name">フォルダ名。</param>
    /// <returns>見つかったノード。</returns>
    private static FolderNodeViewModel LocateFolder(MainViewModel viewModel, string name)
    {
        return viewModel.FolderTree.First().Children.First(child => child.Name == name);
    }

    /// <summary>
    /// テスト用の空ファイルを作る。
    /// </summary>
    /// <param name="composer">作曲家フォルダ名。</param>
    /// <param name="album">アルバムフォルダ名。</param>
    /// <param name="fileName">ファイル名。</param>
    private void CreateTrack(string composer, string album, string fileName)
    {
        string directory = Path.Combine(_root, "library", composer, album);
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
