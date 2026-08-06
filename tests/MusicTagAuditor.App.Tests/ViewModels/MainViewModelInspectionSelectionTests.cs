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
/// 検査結果の選択件数と「チェックした項目を適用」の活性のテスト。
///
/// 守りたいのは「チェックを操作したら、その場で適用に進めるようになる」の一点。
/// これが切れると、既定で選択 0 件だった検査結果から先へ進めなくなり、
/// 検査をやり直す以外にボタンを有効化する手段が無くなる（docs/SPEC.md 5.3 / 9章）。
/// </summary>
public sealed class MainViewModelInspectionSelectionTests : IDisposable
{
    /// <summary>テスト用の作業ディレクトリ。ライブラリ・設定・辞書をここに置く。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "musicTagger-vm-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 作業ディレクトリを用意する。
    /// </summary>
    public MainViewModelInspectionSelectionTests()
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
    /// 明細をチェックすると、その場で適用できるようになる。
    /// </summary>
    [Fact]
    public async Task 明細をチェックすると適用できるようになる()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();
        TagChangeViewModel fixable = ClearAllSelections(viewModel);

        Assert.False(viewModel.CanApplyChanges);
        Assert.False(viewModel.ApplyCommand.CanExecute(null));

        fixable.IsSelected = true;

        Assert.True(viewModel.CanApplyChanges);
        Assert.True(viewModel.ApplyCommand.CanExecute(null));
    }

    /// <summary>
    /// チェックを外すと適用できなくなる。
    /// </summary>
    [Fact]
    public async Task チェックを全部外すと適用できなくなる()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();
        TagChangeViewModel fixable = ClearAllSelections(viewModel);

        fixable.IsSelected = true;
        fixable.IsSelected = false;

        Assert.False(viewModel.CanApplyChanges);
        Assert.False(viewModel.ApplyCommand.CanExecute(null));
    }

    /// <summary>
    /// 要約テキストの選択件数が追従する。
    /// 検査直後だけ「既定で選択」と出し、利用者が触ったあとは「選択」に変える。
    /// </summary>
    [Fact]
    public async Task 要約の選択件数が追従する()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        Assert.Contains("既定で選択", viewModel.InspectionSummary, StringComparison.Ordinal);

        TagChangeViewModel fixable = ClearAllSelections(viewModel);

        Assert.Contains("/ 選択 0 件 /", viewModel.InspectionSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("既定で選択", viewModel.InspectionSummary, StringComparison.Ordinal);

        fixable.IsSelected = true;

        Assert.Contains("/ 選択 1 件 /", viewModel.InspectionSummary, StringComparison.Ordinal);
    }

    /// <summary>
    /// 上段のルール行を切り替えると、下段に表示中の明細のチェックもその場で変わる。
    /// ルールを選び直すまで古い表示が残らないこと。
    ///
    /// 下段のインスタンスが上段の持ち物と同じであることも併せて確かめる。
    /// 別物になっていると、画面は変わっても適用対象が変わらない。
    /// </summary>
    [Fact]
    public async Task ルール行の切り替えが表示中の明細に届く()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        RuleResultViewModel rule = viewModel.RuleResults.First(rule => rule.FixableCount > 0);
        viewModel.SelectedRule = rule;

        Assert.Equal(rule.Changes, viewModel.InspectionChanges);
        Assert.Contains(viewModel.InspectionChanges, change => change.IsSelected);

        rule.IsSelected = false;

        Assert.DoesNotContain(viewModel.InspectionChanges, change => change.IsSelected);

        rule.IsSelected = true;

        Assert.Contains(viewModel.InspectionChanges, change => change.IsSelected);
        Assert.True(viewModel.CanApplyChanges);
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
    /// すべてのチェックを外し、修正値を持つ明細を 1 件返す。
    ///
    /// どのルールが何件出るかは検査ルールの実装に依存するため、
    /// 「既定で選択 0 件」の状態をテスト側で作ってから確かめる。
    /// </summary>
    /// <param name="viewModel">対象のビューモデル。</param>
    /// <returns>修正値を持つ明細のビューモデル。</returns>
    private static TagChangeViewModel ClearAllSelections(MainViewModel viewModel)
    {
        foreach (RuleResultViewModel rule in viewModel.RuleResults)
        {
            foreach (TagChangeViewModel change in rule.Changes)
            {
                change.IsSelected = false;
            }
        }

        Assert.False(viewModel.CanApplyChanges);

        return viewModel.RuleResults
            .SelectMany(rule => rule.Changes)
            .First(change => change.HasFix);
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
