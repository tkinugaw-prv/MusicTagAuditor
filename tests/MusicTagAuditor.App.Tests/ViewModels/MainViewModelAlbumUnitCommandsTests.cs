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
/// 「このアルバムの扱いを決める」で扱う。取り違えると、登録しても検出が減らない作業を延々させる。
/// </summary>
public sealed class MainViewModelAlbumUnitCommandsTests : IDisposable
{
    /// <summary>作曲家が 1 人だけのフォルダ。</summary>
    private const string SINGLE_COMPOSER_FOLDER = @"ブルックナー\ブルックナー 8 - ショルティ";

    /// <summary>作曲家が複数いるフォルダ（3.5 規則5・規則6 の対象）。</summary>
    private const string MIXED_COMPOSER_FOLDER = @"その他\名曲集";

    /// <summary>作品は決まるが <c>date</c> が割れているフォルダ（3.5 規則2 の保留）。</summary>
    private const string SPLIT_DATE_FOLDER = @"ブルックナー\ブルックナー 7 - ヴァント";

    /// <summary>作品は決まるが <c>date</c> が入っていないフォルダ。</summary>
    private const string NO_DATE_FOLDER = @"ブルックナー\ブルックナー 7 - 年なし";

    /// <summary>作品は決まるが <c>artist</c> が割れているフォルダ。</summary>
    private const string SPLIT_ARTIST_FOLDER = @"ブルックナー\ブルックナー 7 - 演奏者違い";

    /// <summary>テスト用の作業ディレクトリ。ライブラリ・設定・辞書をここに置く。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MusicTagAuditor-unit-" + Guid.NewGuid().ToString("N"));

    /// <summary>相対パスごとのタグ。読み取りはここから返す。</summary>
    private readonly Dictionary<string, Dictionary<TagField, string>> _tags = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// ビューモデルに渡した辞書ストア。<see cref="CreateInspectedViewModelAsync"/> が入れる。
    ///
    /// 検査のあとで辞書を変えたいテストが使う。**同じ実体を共有する**ので、ここで保存すれば
    /// 次の検査から新しい索引が効く。
    /// </summary>
    private DictionaryStore? _dictionaryStore;

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
    /// <c>date</c> が割れている保留行では、個別例外の導線を出す（3.5 規則2・規則5）。
    ///
    /// 主作品と併録曲で録音年が違うだけの単位は、フォルダを分けるのも <c>date</c> を揃えるのも
    /// 誤りで、**主作品の年を個別例外に書く以外に解きようがない。**
    /// </summary>
    [Fact]
    public async Task 年が割れている保留行では個別例外の導線を出す()
    {
        AddTrack(Path.Combine(SPLIT_DATE_FOLDER, "01.m4a"), "Anton Bruckner", "Symphony No.7", "1990");
        AddTrack(Path.Combine(SPLIT_DATE_FOLDER, "02.m4a"), "Anton Bruckner", "Symphony No.7", "1991");

        MainViewModel viewModel = await CreateInspectedViewModelAsync(WithBrucknerSeventh);

        TagChangeViewModel change = SelectChange(viewModel, SPLIT_DATE_FOLDER);

        Assert.Equal(HoldReason.DateUnknown, change.Change.HoldReason);
        Assert.True(viewModel.AddAlbumOverrideFromChangeCommand.CanExecute(null));

        // 作品は決まっているので、作品を足す導線のほうは出ない。
        Assert.False(viewModel.AddWorkFromChangeCommand.CanExecute(null));
    }

    /// <summary>
    /// <c>date</c> が未設定の保留行では、個別例外の導線を出さない。
    ///
    /// **年に書けるのは単位内にある値のどれを採るかだけ**で、未設定の単位には選ぶものが無い。
    /// 開いても何もできないので押させない。そちらは CD 実物を確かめてタグに入れる。
    /// </summary>
    [Fact]
    public async Task 年が未設定の保留行では個別例外の導線を出さない()
    {
        AddTrack(Path.Combine(NO_DATE_FOLDER, "01.m4a"), "Anton Bruckner", "Symphony No.7", date: string.Empty);
        AddTrack(Path.Combine(NO_DATE_FOLDER, "02.m4a"), "Anton Bruckner", "Symphony No.7", date: string.Empty);

        MainViewModel viewModel = await CreateInspectedViewModelAsync(WithBrucknerSeventh);

        TagChangeViewModel change = SelectChange(viewModel, NO_DATE_FOLDER);

        Assert.Equal(HoldReason.DateUnknown, change.Change.HoldReason);
        Assert.False(viewModel.AddAlbumOverrideFromChangeCommand.CanExecute(null));
    }

    /// <summary>
    /// <c>artist</c> が割れている保留行では、個別例外の導線を出さない。
    ///
    /// **個別例外に <c>artist</c> は無いので、登録しても保留は解けない。** それでも対象外に
    /// すれば一覧からは消えるため、押せるままにしておくと「タグは割れたまま検出だけ消えた」
    /// 単位ができる（docs/SPEC.md 7.4.4）。
    /// </summary>
    [Fact]
    public async Task 演奏者が決まらない保留行では個別例外の導線を出さない()
    {
        AddTrack(Path.Combine(SPLIT_ARTIST_FOLDER, "01.m4a"), "Anton Bruckner", "Symphony No.7", artist: "Günter Wand");
        AddTrack(Path.Combine(SPLIT_ARTIST_FOLDER, "02.m4a"), "Anton Bruckner", "Symphony No.7", artist: "Georg Solti");

        MainViewModel viewModel = await CreateInspectedViewModelAsync(WithBrucknerSeventh);

        TagChangeViewModel change = SelectChange(viewModel, SPLIT_ARTIST_FOLDER);

        Assert.Equal(HoldReason.ArtistUnknown, change.Change.HoldReason);
        Assert.False(viewModel.AddAlbumOverrideFromChangeCommand.CanExecute(null));
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
    /// 再検査をまたいでも、選んでいたルールと明細が選ばれたままであることを確認する。
    ///
    /// 「このアルバムの扱いを決める」は辞書を保存したあと再検査する。ルール行は毎回作り直されるため、
    /// 参照で選択を追うと必ず先頭へ落ちていた。**どこまで見たかを毎回探し直すことになる。**
    /// 明細は先頭ではなく 2 件目を選んでおく。ルールだけ合わせて先頭の明細を選ぶ実装では通らない。
    /// </summary>
    [Fact]
    public async Task 再検査しても選んでいたルールと明細が残る()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        RuleResultViewModel rule = viewModel.RuleResults.First(rule => rule.RuleId == AlbumNameRule.RULE_ID);

        viewModel.SelectedRule = rule;

        // 先頭の行を選んでいては、先頭へ落ちたのか残ったのかを区別できない。
        Assert.NotSame(viewModel.RuleResults[0], rule);

        TagChangeViewModel change = viewModel.InspectionChanges[1];

        viewModel.SelectedChange = change;

        viewModel.InspectCommand.Execute(null);

        Assert.Equal(rule.RuleId, viewModel.SelectedRule?.RuleId);
        Assert.Equal(change.RelativePath, viewModel.SelectedChange?.RelativePath);
        Assert.Equal(change.Change.Field, viewModel.SelectedChange?.Change.Field);
    }

    /// <summary>
    /// 対象外にして明細が消えたら、上段の選択は残したまま下段は選択なしに戻ることを確認する。
    ///
    /// **消えた行の代わりに別の行を選んではならない。** 選択が黙って隣へずれると、直したつもりの
    /// 無い行を直すことになる。一方で上段まで先頭へ落とすと、対象外にするたびに作業位置を失う。
    /// </summary>
    [Fact]
    public async Task 対象外にして明細が消えても上段の選択は残る()
    {
        MainViewModel viewModel = await CreateInspectedViewModelAsync();

        RuleResultViewModel rule = viewModel.RuleResults.First(rule => rule.RuleId == AlbumNameRule.RULE_ID);

        viewModel.SelectedRule = rule;

        TagChangeViewModel change = viewModel.InspectionChanges.First(
            row => row.RelativePath.StartsWith(MIXED_COMPOSER_FOLDER, StringComparison.Ordinal));

        viewModel.SelectedChange = change;

        // 「このアルバムの扱いを決める → 対象外にする」が辞書へ入れるものと同じ内容。
        // ストアは MainViewModel と同じ実体なので、保存すれば次の検査から新しい索引が効く。
        _dictionaryStore!.Save(DictionaryEditor.AddAlbumOverride(
            _dictionaryStore.Dictionary,
            new AlbumOverrideEntry { Folder = MIXED_COMPOSER_FOLDER, Exclude = true, Note = "テスト" }));

        viewModel.InspectCommand.Execute(null);

        Assert.Equal(rule.RuleId, viewModel.SelectedRule?.RuleId);
        Assert.DoesNotContain(viewModel.InspectionChanges, row => row.RelativePath == change.RelativePath);
        Assert.Null(viewModel.SelectedChange);
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
        TagChangeViewModel change = SelectChange(viewModel, folder);

        Assert.Equal(HoldReason.WorkUnknown, change.Change.HoldReason);
    }

    /// <summary>
    /// 指定フォルダの R-504 の行を選ぶ。保留の種類は問わない。
    /// </summary>
    /// <param name="viewModel">検査まで済ませたビューモデル。</param>
    /// <param name="folder">対象フォルダ。</param>
    /// <returns>選んだ行。</returns>
    private static TagChangeViewModel SelectChange(MainViewModel viewModel, string folder)
    {
        RuleResultViewModel rule = viewModel.RuleResults.First(rule => rule.RuleId == AlbumNameRule.RULE_ID);

        viewModel.SelectedRule = rule;

        TagChangeViewModel change = rule.Changes.First(
            change => change.RelativePath.StartsWith(folder, StringComparison.Ordinal));

        viewModel.SelectedChange = change;

        return change;
    }

    /// <summary>
    /// ライブラリに 1 ファイル足す。既定では作品エントリが無く、アルバム名を組み立てられない値にする。
    /// </summary>
    /// <param name="relativePath">ライブラリルートからの相対パス。</param>
    /// <param name="composer">作曲家。</param>
    /// <param name="album">アルバム名。作品を引かせたい場合だけ変える。</param>
    /// <param name="date">録音年。単位内で割れさせたい場合だけ変える。空欄なら未設定になる。</param>
    /// <param name="artist">演奏者。単位内で割れさせたい場合だけ変える。</param>
    private void AddTrack(
        string relativePath,
        string composer,
        string album = "名曲集",
        string date = "1990",
        string artist = "Georg Solti")
    {
        string fullPath = Path.Combine(_root, "library", relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, []);

        _tags[relativePath] = new Dictionary<TagField, string>
        {
            [TagField.Composer] = composer,
            [TagField.Album] = album,
            [TagField.Artist] = artist,
            [TagField.Date] = date,
        };
    }

    /// <summary>
    /// ブルックナーの交響曲第7番を辞書に足す。作品が決まる状態を作り、
    /// 保留を <c>date</c> と <c>artist</c> の側へ進めるために使う。
    /// </summary>
    /// <param name="dictionary">元の辞書。</param>
    /// <returns>作品を足した辞書。</returns>
    private static TagDictionary WithBrucknerSeventh(TagDictionary dictionary)
    {
        return DictionaryEditor.AddWork(dictionary, "Anton Bruckner", "Symphony No. 7", ["Symphony No.7"]);
    }

    /// <summary>
    /// 検査まで済ませたビューモデルを作る。
    /// </summary>
    /// <param name="edit">検査の前に辞書へ加える変更。作品を引かせたい場合に渡す。</param>
    /// <returns>検査結果を持つビューモデル。</returns>
    private async Task<MainViewModel> CreateInspectedViewModelAsync(Func<TagDictionary, TagDictionary>? edit = null)
    {
        string settingsDirectory = Path.Combine(_root, "settings");
        Directory.CreateDirectory(settingsDirectory);

        DictionaryStore dictionaryStore = new(settingsDirectory);

        _dictionaryStore = dictionaryStore;

        if (edit is not null)
        {
            dictionaryStore.Save(edit(dictionaryStore.Dictionary));
        }

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
