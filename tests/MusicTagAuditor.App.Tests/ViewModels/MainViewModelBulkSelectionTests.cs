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
/// 検査結果タブの一括選択操作（全選択・全選択解除・選択反転）のテスト。
///
/// 検査結果を保持できるようになると、適用のたびに残った項目へのチェック組み直しが増えるため、
/// 1 行ずつの手作業を減らす目的で追加したボタン群。上段（ルール別集計）は全ルール配下、
/// 下段（差分明細）は選択中ルールの明細だけに作用することを確認する。
/// </summary>
public sealed class MainViewModelBulkSelectionTests : IDisposable
{
    /// <summary>テスト用の作業ディレクトリ。ライブラリ・設定・辞書をここに置く。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "musicTagger-vm-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 作業ディレクトリを用意する。
    /// </summary>
    public MainViewModelBulkSelectionTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "library", "ブルックナー", "ブルックナー 8 - ショルティ"));
        File.WriteAllBytes(
            Path.Combine(_root, "library", "ブルックナー", "ブルックナー 8 - ショルティ", "01 Allegro moderato.m4a"),
            []);
        File.WriteAllBytes(
            Path.Combine(_root, "library", "ブルックナー", "ブルックナー 8 - ショルティ", "02 Scherzo.m4a"),
            []);
    }

    /// <summary>
    /// 上段の「全選択解除」→「全選択」で、全ルール配下の修正可能な項目が一括で切り替わることを確認する。
    /// </summary>
    [Fact]
    public async Task 上段の全選択と全選択解除が全項目に効く()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        viewModel.DeselectAllRuleChangesCommand.Execute(null);

        Assert.DoesNotContain(
            viewModel.RuleResults.SelectMany(rule => rule.Changes),
            change => change.HasFix && change.IsSelected);

        viewModel.SelectAllRuleChangesCommand.Execute(null);

        Assert.DoesNotContain(
            viewModel.RuleResults.SelectMany(rule => rule.Changes).Where(change => change.HasFix),
            change => !change.IsSelected);
    }

    /// <summary>
    /// 上段の「選択反転」が、ヘッダーの一括トグルではなく明細単位で反転することを確認する。
    /// 一部だけ選択済みの状態から実行し、選択・未選択が入れ替わることを見る。
    /// </summary>
    [Fact]
    public async Task 上段の選択反転は明細単位で反転する()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        TagChangeViewModel[] fixable = [.. viewModel.RuleResults.SelectMany(rule => rule.Changes).Where(change => change.HasFix)];
        Assert.True(fixable.Length >= 2, "テストの前提として修正可能な項目が 2 件以上必要");

        // 一部だけ選択済みの混在状態を作る。
        fixable[0].IsSelected = true;
        fixable[1].IsSelected = false;

        bool[] before = [.. fixable.Select(change => change.IsSelected)];

        viewModel.InvertRuleChangesCommand.Execute(null);

        bool[] after = [.. fixable.Select(change => change.IsSelected)];

        Assert.All(Enumerable.Range(0, fixable.Length), i => Assert.Equal(!before[i], after[i]));
    }

    /// <summary>
    /// 下段の3ボタンは選択中ルールの明細だけに作用し、他のルールの項目には影響しないことを確認する。
    /// </summary>
    [Fact]
    public async Task 下段の操作は選択中ルールだけに作用する()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        Assert.True(viewModel.RuleResults.Count >= 2, "テストの前提としてルールが 2 件以上必要");

        RuleResultViewModel selected = viewModel.RuleResults.First(rule => rule.Changes.Any(change => change.HasFix));
        RuleResultViewModel other = viewModel.RuleResults.First(rule => rule != selected && rule.Changes.Any(change => change.HasFix));

        viewModel.SelectedRule = selected;

        bool[] otherBefore = [.. other.Changes.Where(change => change.HasFix).Select(change => change.IsSelected)];

        viewModel.DeselectAllChangesCommand.Execute(null);

        Assert.DoesNotContain(viewModel.InspectionChanges.Where(change => change.HasFix), change => change.IsSelected);

        bool[] otherAfter = [.. other.Changes.Where(change => change.HasFix).Select(change => change.IsSelected)];
        Assert.Equal(otherBefore, otherAfter);
    }

    /// <summary>
    /// 下段の「選択反転」も明細単位で反転することを確認する。
    /// </summary>
    [Fact]
    public async Task 下段の選択反転は明細単位で反転する()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        RuleResultViewModel rule = viewModel.RuleResults.First(rule => rule.Changes.Count(change => change.HasFix) >= 2);
        viewModel.SelectedRule = rule;

        TagChangeViewModel[] fixable = [.. viewModel.InspectionChanges.Where(change => change.HasFix)];
        fixable[0].IsSelected = true;
        fixable[1].IsSelected = false;

        bool[] before = [.. fixable.Select(change => change.IsSelected)];

        viewModel.InvertChangesCommand.Execute(null);

        bool[] after = [.. fixable.Select(change => change.IsSelected)];

        Assert.All(Enumerable.Range(0, fixable.Length), i => Assert.Equal(!before[i], after[i]));
    }

    /// <summary>
    /// 一括選択操作のあとも、選択件数表示（<see cref="MainViewModel.InspectionSummary"/>）と
    /// 適用可否（<see cref="MainViewModel.CanApplyChanges"/>）が正しく追従することを確認する。
    /// </summary>
    [Fact]
    public async Task 一括選択操作後も選択件数と適用可否が追従する()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        viewModel.DeselectAllRuleChangesCommand.Execute(null);

        Assert.False(viewModel.CanApplyChanges);
        Assert.Contains("選択 0 件", viewModel.InspectionSummary, StringComparison.Ordinal);

        viewModel.SelectAllRuleChangesCommand.Execute(null);

        int fixableCount = viewModel.RuleResults.SelectMany(rule => rule.Changes).Count(change => change.HasFix);

        Assert.True(viewModel.CanApplyChanges);
        Assert.Contains($"選択 {fixableCount:N0} 件", viewModel.InspectionSummary, StringComparison.Ordinal);
    }

    /// <summary>
    /// 選択反転のあと、上段のルール行のチェック（ヘッダー）が配下の実態に追従することを確認する。
    ///
    /// ヘッダーが取り残されると、画面上は「選択反転を押しても何も起きない」ように見える。
    /// </summary>
    [Fact]
    public async Task 選択反転で上段のヘッダーチェックも追従する()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        RuleResultViewModel rule = viewModel.RuleResults.First(rule => rule.FixableCount > 0);

        // 全解除 → ヘッダーも false。
        viewModel.DeselectAllRuleChangesCommand.Execute(null);
        Assert.False(rule.IsSelected);

        // 反転 → 配下が全選択になるのでヘッダーも true。
        viewModel.InvertRuleChangesCommand.Execute(null);

        Assert.DoesNotContain(rule.Changes.Where(change => change.HasFix), change => !change.IsSelected);
        Assert.True(rule.IsSelected);

        // もう一度反転 → 配下が全解除になるのでヘッダーも false。
        viewModel.InvertRuleChangesCommand.Execute(null);

        Assert.DoesNotContain(rule.Changes.Where(change => change.HasFix), change => change.IsSelected);
        Assert.False(rule.IsSelected);
    }

    /// <summary>
    /// 明細を 1 件だけ外したとき、上段のヘッダーが false になり、
    /// **かつ配下の他の項目まで巻き込んで解除されない**ことを確認する。
    ///
    /// ヘッダー同期が配下へ撃ち返すと「1 件外した」が「全件外す」に化ける。
    /// </summary>
    [Fact]
    public async Task 明細を1件外してもヘッダー同期が他を巻き込まない()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        RuleResultViewModel rule = viewModel.RuleResults.First(rule => rule.Changes.Count(change => change.HasFix) >= 2);

        viewModel.SelectedRule = rule;
        viewModel.SelectAllChangesCommand.Execute(null);

        Assert.True(rule.IsSelected);

        TagChangeViewModel[] fixable = [.. rule.Changes.Where(change => change.HasFix)];
        fixable[0].IsSelected = false;

        // ヘッダーは「全件チェック済み」ではなくなったので false。
        Assert.False(rule.IsSelected);

        // 残りは選択されたまま。
        Assert.All(fixable.Skip(1), change => Assert.True(change.IsSelected));
    }

    /// <summary>
    /// 対象コレクションが空のときは、各コマンドの <c>CanExecute</c> が false になることを確認する。
    /// </summary>
    [Fact]
    public void 対象が空なら一括選択コマンドは実行できない()
    {
        // 検査前（RuleResults / InspectionChanges が空）のビューモデルで確認する。
        string settingsDirectory = Path.Combine(_root, "settings-empty");
        Directory.CreateDirectory(settingsDirectory);

        DictionaryStore dictionaryStore = new(settingsDirectory);
        SnapshotService snapshotService = new(() => Path.Combine(_root, "backup-empty"));
        EmptyTagReader tagReader = new();
        NullTagWriter tagWriter = new();

        MainViewModel emptyViewModel = new(
            new LibraryScanner(tagReader),
            snapshotService,
            new RestoreService(tagWriter, tagReader),
            new InspectionEngine(),
            dictionaryStore,
            new DictionaryViewModel(dictionaryStore),
            new ApplyService(tagWriter, tagReader, snapshotService),
            new AppSettingsStore(settingsDirectory));

        Assert.Empty(emptyViewModel.RuleResults);
        Assert.Empty(emptyViewModel.InspectionChanges);

        Assert.False(emptyViewModel.SelectAllRuleChangesCommand.CanExecute(null));
        Assert.False(emptyViewModel.DeselectAllRuleChangesCommand.CanExecute(null));
        Assert.False(emptyViewModel.InvertRuleChangesCommand.CanExecute(null));
        Assert.False(emptyViewModel.SelectAllChangesCommand.CanExecute(null));
        Assert.False(emptyViewModel.DeselectAllChangesCommand.CanExecute(null));
        Assert.False(emptyViewModel.InvertChangesCommand.CanExecute(null));
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
