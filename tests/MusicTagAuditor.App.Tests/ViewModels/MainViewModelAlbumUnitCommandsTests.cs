using System.IO;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Abstractions;
using MusicTagAuditor.Core.Applying;
using MusicTagAuditor.Core.Backup;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Inspection.Rules;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;
using MusicTagAuditor.Core.Settings;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// 検査結果からの 2 つの導線が、出る行と出ない行を取り違えないことのテスト（docs/SPEC.md 7.3.2）。
///
/// **単位内に作曲家が複数ある行で「作品を辞書に追加」を出してはならない。** 作品を足しても保留は
/// 解けず、主作品を機械では決められない（TAGGING_POLICY 3.5 規則5・規則6）。そこは
/// 「このアルバムを対象外にする」で扱う。取り違えると、登録しても検出が減らない作業を延々させる。
/// </summary>
public sealed class MainViewModelAlbumUnitCommandsTests : IDisposable
{
    /// <summary>作曲家が 1 人だけのフォルダ。</summary>
    private const string SINGLE_COMPOSER_FOLDER = @"ブルックナー\ブルックナー 8 - ショルティ";

    /// <summary>作曲家が複数いるフォルダ（3.5 規則5・規則6 の対象）。</summary>
    private const string MIXED_COMPOSER_FOLDER = @"その他\名曲集";

    /// <summary>テスト用の作業ディレクトリ。ライブラリ・設定・辞書をここに置く。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MusicTagAuditor-unit-" + Guid.NewGuid().ToString("N"));

    /// <summary>相対パスごとのタグ。読み取りはここから返す。</summary>
    private readonly Dictionary<string, Dictionary<TagField, string>> _tags = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// ライブラリを用意する。
    /// </summary>
    public MainViewModelAlbumUnitCommandsTests()
    {
        AddTrack(Path.Combine(SINGLE_COMPOSER_FOLDER, "01.m4a"), "Anton Bruckner");
        AddTrack(Path.Combine(SINGLE_COMPOSER_FOLDER, "02.m4a"), "Anton Bruckner");
        AddTrack(Path.Combine(MIXED_COMPOSER_FOLDER, "01.m4a"), "Anton Bruckner");
        AddTrack(Path.Combine(MIXED_COMPOSER_FOLDER, "02.m4a"), "Johannes Brahms");
    }

    /// <summary>
    /// 作曲家が 1 人に定まる保留行では、両方の導線を出す。
    /// </summary>
    [Fact]
    public async Task 作曲家が定まる保留行では作品を追加できる()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        SelectHold(viewModel, SINGLE_COMPOSER_FOLDER);

        Assert.True(viewModel.AddWorkFromChangeCommand.CanExecute(null));
        Assert.True(viewModel.AddAlbumOverrideFromChangeCommand.CanExecute(null));
    }

    /// <summary>
    /// 作曲家が複数いる保留行では、作品の追加は出さず、対象外の導線だけを出す。
    /// </summary>
    [Fact]
    public async Task 作曲家が複数の保留行では対象外の導線だけを出す()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        SelectHold(viewModel, MIXED_COMPOSER_FOLDER);

        Assert.False(viewModel.AddWorkFromChangeCommand.CanExecute(null));
        Assert.True(viewModel.AddAlbumOverrideFromChangeCommand.CanExecute(null));
    }

    /// <summary>
    /// アルバム名以外のルールの行では、どちらの導線も出さない。
    /// これらはアルバム単位に紐づく操作であり、1 ファイルの値の話ではない。
    /// </summary>
    [Fact]
    public async Task 別のルールの行では導線を出さない()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        RuleResultViewModel rule = viewModel.RuleResults.First(
            rule => rule.RuleId != AlbumNameRule.RULE_ID && rule.RuleId != AlbumNameCollisionRule.RULE_ID);

        viewModel.SelectedRule = rule;
        viewModel.SelectedChange = rule.Changes[0];

        Assert.False(viewModel.AddWorkFromChangeCommand.CanExecute(null));
        Assert.False(viewModel.AddAlbumOverrideFromChangeCommand.CanExecute(null));
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
    /// 指定フォルダの R-504 保留行を選ぶ。
    /// </summary>
    private static void SelectHold(MainViewModel viewModel, string folder)
    {
        RuleResultViewModel rule = viewModel.RuleResults.First(rule => rule.RuleId == AlbumNameRule.RULE_ID);

        viewModel.SelectedRule = rule;

        TagChangeViewModel change = rule.Changes.First(
            change => change.RelativePath.StartsWith(folder, StringComparison.Ordinal));

        Assert.Equal(HoldReason.WorkUnknown, change.Change.HoldReason);

        viewModel.SelectedChange = change;
    }

    /// <summary>
    /// ライブラリに 1 ファイル足す。アルバム名は組み立てられない値にしておく（作品エントリが無い）。
    /// </summary>
    private void AddTrack(string relativePath, string composer)
    {
        string fullPath = Path.Combine(_root, "library", relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, []);

        _tags[relativePath] = new Dictionary<TagField, string>
        {
            [TagField.Composer] = composer,
            [TagField.Album] = "名曲集",
            [TagField.Artist] = "Georg Solti",
            [TagField.Date] = "1990",
        };
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
        StubTagReader tagReader = new(_tags);
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
    /// 用意しておいたタグを返すタグリーダー。実ファイルは読まない。
    /// </summary>
    private sealed class StubTagReader : ITagReader
    {
        /// <summary>相対パスごとのタグ。</summary>
        private readonly Dictionary<string, Dictionary<TagField, string>> _tags;

        /// <summary>
        /// タグを渡してリーダーを作る。
        /// </summary>
        /// <param name="tags">相対パスごとのタグ。</param>
        public StubTagReader(Dictionary<string, Dictionary<TagField, string>> tags)
        {
            _tags = tags;
        }

        /// <inheritdoc />
        public TrackTags Read(string fullPath, string relativePath)
        {
            Dictionary<TagField, string> fields = _tags.TryGetValue(relativePath, out Dictionary<TagField, string>? found)
                ? found
                : [];

            return new TrackTags
            {
                RelativePath = relativePath,
                FullPath = fullPath,
                Format = AudioFormat.M4a,
                Fields = TrackTags.BuildFields(
                    fields.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)new[] { pair.Value })),
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
        public void Write(string fullPath, IReadOnlyDictionary<TagField, IReadOnlyList<string>> values)
        {
        }
    }
}
