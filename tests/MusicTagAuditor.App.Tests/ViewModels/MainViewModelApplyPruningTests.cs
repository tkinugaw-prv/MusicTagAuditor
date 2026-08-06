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
/// 適用に成功した項目だけを検査結果から取り除く挙動（<see cref="MainViewModel.RemoveSucceededFromInspection"/>）のテスト。
///
/// 「適用のたびに検査結果タブが全クリアされて使いにくい」という指摘への対応。
/// 守りたいのは「成功項目は消える」「それ以外（未対象・失敗・不一致・競合）はチェック状態ごと残る」の 2 点。
///
/// <see cref="MainViewModel.ApplyCommand"/> 自体は確認ダイアログ（<c>MessageBox.Show</c>）を経由するため
/// 自動テストで直接実行できない。そのため <c>internal</c> 化した
/// <see cref="MainViewModel.RemoveSucceededFromInspection"/> を直接呼んで検証する。
/// </summary>
public sealed class MainViewModelApplyPruningTests : IDisposable
{
    /// <summary>テスト用の作業ディレクトリ。ライブラリ・設定・辞書をここに置く。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "musicTagger-vm-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 作業ディレクトリを用意する。複数ルールが検出されるよう、フォルダ名から作曲家が拾えるうえに
    /// genre が未設定のファイルを 2 件用意する。
    /// </summary>
    public MainViewModelApplyPruningTests()
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
    /// 成功した項目だけが検査結果から消え、対象外の項目は残ることを確認する。
    /// </summary>
    [Fact]
    public async Task 成功した項目だけが消え他は残る()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        TagChange[] fixable = [.. viewModel.RuleResults.SelectMany(rule => rule.Changes)
            .Where(change => change.HasFix)
            .Select(change => change.Change)];

        Assert.True(fixable.Length >= 2, "テストの前提として修正可能な項目が 2 件以上必要");

        TagChange succeeded = fixable[0];
        TagChange remaining = fixable[1];

        viewModel.RemoveSucceededFromInspection(new HashSet<TagChangeKey> { TagChangeKey.From(succeeded) });

        TagChange[] afterAll = [.. viewModel.RuleResults.SelectMany(rule => rule.Changes).Select(change => change.Change)];

        Assert.DoesNotContain(afterAll, change => TagChangeKey.From(change) == TagChangeKey.From(succeeded));
        Assert.Contains(afterAll, change => TagChangeKey.From(change) == TagChangeKey.From(remaining));
    }

    /// <summary>
    /// 触れていないルール行は、ビューモデルのインスタンスが変わらないことを確認する。
    /// ヘッダーのチェック状態を意味なくリセットしないため。
    /// </summary>
    [Fact]
    public async Task 触れていないルール行はインスタンスが変わらない()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        Assert.True(viewModel.RuleResults.Count >= 2, "テストの前提としてルールが 2 件以上必要");

        RuleResultViewModel touchedRule = viewModel.RuleResults.First(rule => rule.Changes.Any(change => change.HasFix));
        RuleResultViewModel untouchedRule = viewModel.RuleResults.First(rule => rule != touchedRule);

        TagChange target = touchedRule.Changes.First(change => change.HasFix).Change;

        viewModel.RemoveSucceededFromInspection(new HashSet<TagChangeKey> { TagChangeKey.From(target) });

        Assert.Contains(viewModel.RuleResults, rule => ReferenceEquals(rule, untouchedRule));
    }

    /// <summary>
    /// 選択中ルールの全項目が成功すると、そのルール行は上段から消え、
    /// 選択が別のルールへ自動的に切り替わることを確認する。
    /// </summary>
    [Fact]
    public async Task 選択中ルールが全消化されると別ルールへ切り替わる()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        Assert.True(viewModel.RuleResults.Count >= 2, "テストの前提としてルールが 2 件以上必要");

        RuleResultViewModel targetRule = viewModel.RuleResults.First(rule => rule.Changes.All(change => change.HasFix));
        viewModel.SelectedRule = targetRule;

        HashSet<TagChangeKey> keys = [.. targetRule.Changes.Select(change => TagChangeKey.From(change.Change))];

        viewModel.RemoveSucceededFromInspection(keys);

        Assert.DoesNotContain(viewModel.RuleResults, rule => ReferenceEquals(rule, targetRule));
        Assert.NotNull(viewModel.SelectedRule);
        Assert.NotSame(targetRule, viewModel.SelectedRule);
    }

    /// <summary>
    /// 選択中ルールが一部だけ残った場合、下段の明細表示が新しいインスタンスへ追従することを確認する。
    /// </summary>
    [Fact]
    public async Task 選択中ルールが一部残ると下段の表示が追従する()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        RuleResultViewModel targetRule = viewModel.RuleResults.First(rule => rule.Changes.Count(change => change.HasFix) >= 2);
        viewModel.SelectedRule = targetRule;

        TagChangeViewModel removed = targetRule.Changes.First(change => change.HasFix);

        viewModel.RemoveSucceededFromInspection(new HashSet<TagChangeKey> { TagChangeKey.From(removed.Change) });

        Assert.NotSame(targetRule, viewModel.SelectedRule);
        Assert.DoesNotContain(viewModel.InspectionChanges, change => TagChangeKey.From(change.Change) == TagChangeKey.From(removed.Change));
    }

    /// <summary>
    /// 選択件数の表示（<see cref="MainViewModel.InspectionSummary"/>）と
    /// 適用可否（<see cref="MainViewModel.CanApplyChanges"/>）が刈り込み後も正しく追従することを確認する。
    /// </summary>
    [Fact]
    public async Task 刈り込み後も選択件数の表示が追従する()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        TagChange[] fixable = [.. viewModel.RuleResults.SelectMany(rule => rule.Changes)
            .Where(change => change.HasFix)
            .Select(change => change.Change)];

        Assert.True(fixable.Length >= 1);

        int selectedBefore = fixable.Count(change => change.IsSelected);
        TagChange target = fixable.First(change => change.IsSelected);

        viewModel.RemoveSucceededFromInspection(new HashSet<TagChangeKey> { TagChangeKey.From(target) });

        int selectedAfter = viewModel.RuleResults.SelectMany(rule => rule.Changes).Count(change => change.IsSelected);

        Assert.Equal(selectedBefore - 1, selectedAfter);
        Assert.Contains($"選択 {selectedAfter:N0} 件", viewModel.InspectionSummary, StringComparison.Ordinal);
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
