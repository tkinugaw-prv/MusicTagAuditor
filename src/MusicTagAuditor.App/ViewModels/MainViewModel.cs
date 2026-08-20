using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MusicTagAuditor.Core.Applying;
using MusicTagAuditor.Core.Backup;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Editing;
using MusicTagAuditor.Core.Export;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Inspection.Rules;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;
using MusicTagAuditor.Core.Settings;
using MusicTagAuditor.TagIo;
using Serilog;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// メインウィンドウのビューモデル。
/// 段階 5 までの範囲。スキャンと読み取り、一覧表示、バックアップと復元、検査と適用、
/// 辞書の閲覧・編集を扱う。手編集は段階 6 以降（docs/SPEC.md 12章）。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>バックアップ先が書けるかを確かめる一時ファイルの接頭辞。</summary>
    private const string WRITE_PROBE_FILE_PREFIX = ".musictagauditor_write_test_";

    /// <summary>ライブラリスキャナ。</summary>
    private readonly LibraryScanner _scanner;

    /// <summary>スナップショットの取得・読み込み。</summary>
    private readonly SnapshotService _snapshotService;

    /// <summary>復元処理。</summary>
    private readonly RestoreService _restoreService;

    /// <summary>検査エンジン。</summary>
    private readonly InspectionEngine _inspectionEngine;

    /// <summary>正規化辞書の保持と保存。**索引は握らず、必要になるたびに取り直す。**</summary>
    private readonly DictionaryStore _dictionaryStore;

    /// <summary>適用処理。</summary>
    private readonly ApplyService _applyService;

    /// <summary>アプリ設定の保持と保存。</summary>
    private readonly AppSettingsStore _settingsStore;

    /// <summary>直近の検査結果。適用対象はここから取る。</summary>
    private InspectionResult? _lastInspection;

    /// <summary>直近のスキャン結果。バックアップの取得元になる。</summary>
    private ScanResult? _lastScan;

    /// <summary>
    /// 直近の検査に使った文脈。**アルバム単位の出どころ**（docs/SPEC.md 7.3.2）。
    ///
    /// 作品の追加と個別例外は明細 1 行ではなくアルバム単位に紐づくので、
    /// 明細から単位へ辿り直せるように検査の文脈を持っておく。
    /// </summary>
    private InspectionContext? _lastContext;

    /// <summary>直近のスキャン結果。ツリーでの絞り込みはこの一覧から行う。</summary>
    private IReadOnlyList<TrackRowViewModel> _allTracks = [];

    /// <summary>
    /// 検査で出た全ルール行。<see cref="RuleResults"/> はここから絞り込んだ表示用の写し。
    /// <see cref="_allTracks"/> と <see cref="Tracks"/> の関係と同じ。
    /// </summary>
    private readonly List<RuleResultViewModel> _allRuleResults = [];

    /// <summary>
    /// 保留中の手編集（段階 6）。**セルを直してもここに溜めるだけで、ファイルには書き込まない。**
    /// </summary>
    private readonly ManualEditSet _manualEdits = new();

    /// <summary>ファイル一覧の絞り込みビュー。</summary>
    private ICollectionView? _trackView;

    /// <summary>
    /// ファイル一覧の絞り込みを掛け直す係。**編集中は掛け直さない**（<see cref="GridViewRefresher"/>）。
    /// </summary>
    private GridViewRefresher? _trackRefresher;

    /// <summary>検査結果の差分明細（下段）の絞り込みビュー。</summary>
    private ICollectionView? _inspectionChangeView;

    /// <summary>
    /// 差分明細の絞り込みを掛け直す係。**編集中は掛け直さない**（<see cref="GridViewRefresher"/>）。
    /// チェックボックス列を触ると絞り込みの条件そのものが変わるため、ここが特に効く。
    /// </summary>
    private GridViewRefresher? _inspectionChangeRefresher;

    /// <summary>実行中のスキャンをキャンセルするためのトークンソース。</summary>
    private CancellationTokenSource? _scanCancellation;

    /// <summary>開いているライブラリのルート。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RescanCommand))]
    [NotifyPropertyChangedFor(nameof(PathSummary))]
    private string? _libraryRoot;

    /// <summary>
    /// ライブラリ・バックアップ先のパス欄を畳んでいるか（docs/SPEC.md 5.1）。
    ///
    /// 開いた後は触らない欄なので、畳んで下の一覧に高さを回せるようにしてある。
    /// 値は <see cref="AppSettings.PathsCollapsed"/> に残り、次の起動へ引き継がれる。
    /// </summary>
    [ObservableProperty]
    private bool _arePathsCollapsed;

    /// <summary>
    /// バックアップの保存先。空欄ならライブラリ直下（従来の動作）。
    /// **表示専用。** 変更は <see cref="ChangeBackupRootCommand"/> か
    /// <see cref="ResetBackupRootCommand"/> を通す。検証を通さずに設定へ書きたくない。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetBackupRootCommand))]
    private string? _backupRoot;

    /// <summary>スキャン中かどうか。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLibraryCommand))]
    [NotifyCanExecuteChangedFor(nameof(RescanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewRestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(InspectCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddUnknownValueToDictionaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddSelectedChangeToDictionaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddWorkFromChangeCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddAlbumOverrideFromChangeCommand))]
    private bool _isScanning;

    /// <summary>進捗の現在値。</summary>
    [ObservableProperty]
    private int _progressValue;

    /// <summary>進捗の最大値。</summary>
    [ObservableProperty]
    private int _progressMaximum = 1;

    /// <summary>ステータスバーに出す文言。</summary>
    [ObservableProperty]
    private string _statusText = "ライブラリを開いてください。";

    /// <summary>ツリーで選択中のフォルダ。</summary>
    [ObservableProperty]
    private FolderNodeViewModel? _selectedFolder;

    /// <summary>
    /// 検査結果をツリーで選択中のフォルダ配下だけに絞るか（docs/SPEC.md 5.3）。
    ///
    /// **既定は無効。** ツリーはファイル一覧タブの操作にも使うため、フォルダを選んだだけで
    /// 検査結果が黙って狭まると、全体を見ているつもりの利用者を欺く。
    /// </summary>
    [ObservableProperty]
    private bool _limitInspectionToSelectedFolder;

    /// <summary>バックアップ履歴で選択中の項目。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewRestoreCommand))]
    private BackupEntryViewModel? _selectedBackup;

    /// <summary>復元計画の説明。</summary>
    [ObservableProperty]
    private string _restoreSummary = "バックアップを選んで「差分を確認」を押してください。";

    /// <summary>復元を実行できる状態か。差分を確認するまでは実行させない。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private bool _isRestoreReady;

    /// <summary>検査結果で選択中のルール。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectAllChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeselectAllChangesCommand))]
    [NotifyCanExecuteChangedFor(nameof(InvertChangesCommand))]
    private RuleResultViewModel? _selectedRule;

    /// <summary>検査結果の要約。</summary>
    [ObservableProperty]
    private string _inspectionSummary = "「検査」を押すと原則違反を洗い出します。";

    /// <summary>適用できる状態か。検査するまでは適用させない。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _canApplyChanges;

    /// <summary>検査結果があるか。CSV 出力は検出が 0 件でも意味があるので、選択件数とは別に持つ。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCsvCommand))]
    private bool _hasInspectionResult;

    /// <summary>読み取ったファイルがあるか。ファイル一覧の CSV 出力は検査を待たずに使える。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportTrackCsvCommand))]
    private bool _hasTracks;

    /// <summary>
    /// 差分明細をチェック済みの行だけに絞るか（docs/SPEC.md 5.3）。
    ///
    /// **表示だけの絞り込み。** 隠すのは適用されない行だけなので、上段の件数・適用対象・
    /// CSV 出力は動かさない。フォルダの絞り込み（<see cref="LimitInspectionToSelectedFolder"/>）が
    /// 対象範囲そのものを狭めるのとはそこが違う。絞っているあいだは、要約の「選択 N 件」と
    /// 画面に出ている行数が一致する。
    ///
    /// **既定は無効。** 検査直後は既定のチェックを見直すところから始まるので、
    /// 未チェックの行が最初から隠れていては選びようがない。
    /// </summary>
    [ObservableProperty]
    private bool _showOnlySelectedChanges;

    /// <summary>検査結果の差分明細で選択中の行。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSelectedChangeToDictionaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddWorkFromChangeCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddAlbumOverrideFromChangeCommand))]
    private TagChangeViewModel? _selectedChange;

    /// <summary>未知の値の一覧で選択中の行。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddUnknownValueToDictionaryCommand))]
    private UnknownValue? _selectedUnknownValue;

    /// <summary>未知の値の要約。</summary>
    [ObservableProperty]
    private string _unknownValueSummary = "検査すると、辞書に無い値がここに集まります。";

    /// <summary>
    /// R-304（曲名中の発音区別符号の欠落）を有効にするか。
    /// 誤検出が増えるため既定で無効（docs/SPEC.md 6.2）。
    /// </summary>
    [ObservableProperty]
    private bool _enableDiacriticCheck;

    /// <summary>ファイル一覧の絞り込み文字列（docs/SPEC.md 5.2）。</summary>
    [ObservableProperty]
    private string _trackFilterText = string.Empty;

    /// <summary>いずれかのタグが空欄の行だけを出すか。R-401 / R-402 の対象を探すのに使う。</summary>
    [ObservableProperty]
    private bool _showOnlyEmptyFields;

    /// <summary>編集した行だけを出すか。</summary>
    [ObservableProperty]
    private bool _showOnlyEditedTracks;

    /// <summary>一括入力の対象フィールド。</summary>
    [ObservableProperty]
    private TagField _bulkField = TagField.Conductor;

    /// <summary>一括入力する値。</summary>
    [ObservableProperty]
    private string _bulkValue = string.Empty;

    /// <summary>手編集の要約。</summary>
    [ObservableProperty]
    private string _manualEditSummary = "セルを直すと、ここに保留中の編集が集まります。";

    /// <summary>保留中の手編集があるか。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyManualEditsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardManualEditsCommand))]
    private bool _hasManualEdits;

    /// <summary>
    /// ビューモデルを初期化する。
    /// </summary>
    /// <param name="scanner">ライブラリスキャナ。</param>
    /// <param name="snapshotService">スナップショットの取得・読み込み。</param>
    /// <param name="restoreService">復元処理。</param>
    /// <param name="inspectionEngine">検査エンジン。</param>
    /// <param name="dictionaryStore">正規化辞書の保持と保存。</param>
    /// <param name="dictionaryViewModel">辞書タブ。</param>
    /// <param name="applyService">適用処理。</param>
    /// <param name="settingsStore">アプリ設定の保持と保存。</param>
    public MainViewModel(
        LibraryScanner scanner,
        SnapshotService snapshotService,
        RestoreService restoreService,
        InspectionEngine inspectionEngine,
        DictionaryStore dictionaryStore,
        DictionaryViewModel dictionaryViewModel,
        ApplyService applyService,
        AppSettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(dictionaryViewModel);
        ArgumentNullException.ThrowIfNull(settingsStore);

        _scanner = scanner;
        _snapshotService = snapshotService;
        _restoreService = restoreService;
        _inspectionEngine = inspectionEngine;
        _dictionaryStore = dictionaryStore;
        _applyService = applyService;
        _settingsStore = settingsStore;

        _backupRoot = settingsStore.Current.BackupRoot;

        // **プロパティではなくフィールドへ入れる。** プロパティ経由だと構築中に
        // OnArePathsCollapsedChanged が走り、読んだばかりの値をそのまま書き戻す。
        _arePathsCollapsed = settingsStore.Current.PathsCollapsed;

        Dictionary = dictionaryViewModel;

        // 辞書を保存したら検査をやり直す。タグは変わっていないので再スキャンは要らない。
        Dictionary.Saved += OnDictionarySaved;

        _manualEdits.Changed += OnManualEditsChanged;

        // 上段の一括選択ボタンの活性はルールの有無で決まる。ObservableCollection は
        // ObservableProperty ではないため [NotifyCanExecuteChangedFor] が使えず、
        // CollectionChanged を購読して手動で再評価する。
        // 下段は SelectedRule に依存するので、そちらの属性に任せてある。
        RuleResults.CollectionChanged += (_, _) =>
        {
            SelectAllRuleChangesCommand.NotifyCanExecuteChanged();
            DeselectAllRuleChangesCommand.NotifyCanExecuteChanged();
            InvertRuleChangesCommand.NotifyCanExecuteChanged();
        };

        RefreshSuggestions();
    }

    /// <summary>
    /// ファイル一覧で特定の行を見せてほしいという要求。
    ///
    /// タブの切り替えとスクロールは View の仕事なので、ビューモデルからは要求だけを出す。
    /// </summary>
    public event EventHandler<TrackRowViewModel>? TrackRevealRequested;

    /// <summary>辞書タブ。</summary>
    public DictionaryViewModel Dictionary { get; }

    /// <summary>一括入力で選べるフィールド。</summary>
    public IReadOnlyList<TagField> EditableFields => ManualEditConst.EDITABLE_FIELDS;

    /// <summary>作曲家欄に出す候補。</summary>
    public IReadOnlyList<SuggestionEntry> ComposerSuggestions { get; private set; } = [];

    /// <summary>アーティスト欄・指揮者欄に出す候補。</summary>
    public IReadOnlyList<SuggestionEntry> PersonSuggestions { get; private set; } = [];

    /// <summary>アルバムアーティスト欄に出す候補。</summary>
    public IReadOnlyList<SuggestionEntry> EnsembleSuggestions { get; private set; } = [];

    /// <summary>一括入力の欄に出す候補。対象フィールドに追従する。</summary>
    public IReadOnlyList<SuggestionEntry> BulkSuggestions { get; private set; } = [];

    /// <summary>保留中の手編集の差分。**適用前に必ずここで確認できる。**</summary>
    public ObservableCollection<TagChange> ManualEditChanges { get; } = [];

    /// <summary>手編集で気づいてほしい点。止めはしない。</summary>
    public ObservableCollection<ManualEditWarning> ManualEditWarnings { get; } = [];

    /// <summary>フォルダツリー。ルートノード 1 件を持つ。</summary>
    public ObservableCollection<FolderNodeViewModel> FolderTree { get; } = [];

    /// <summary>ファイル一覧タブに表示する行。</summary>
    public ObservableCollection<TrackRowViewModel> Tracks { get; } = [];

    /// <summary>読み取りに失敗したファイル。</summary>
    public ObservableCollection<ScanFailure> Failures { get; } = [];

    /// <summary>バックアップ履歴。</summary>
    public ObservableCollection<BackupEntryViewModel> Backups { get; } = [];

    /// <summary>復元で戻る項目。**復元前に必ずこれを見せる**（docs/SPEC.md 8.3）。</summary>
    public ObservableCollection<RestoreItem> RestoreItems { get; } = [];

    /// <summary>検査結果タブ上段。ルール別の集計。</summary>
    public ObservableCollection<RuleResultViewModel> RuleResults { get; } = [];

    /// <summary>検査結果タブ下段。選択したルールの差分明細。</summary>
    public ObservableCollection<TagChangeViewModel> InspectionChanges { get; } = [];

    /// <summary>
    /// 辞書に無いために修正案を出せなかった値（docs/SPEC.md 7.3）。
    ///
    /// **明細ではなく値単位でまとめる。** 同じ値が何ファイルに散っていても登録は 1 回で済む。
    /// </summary>
    public ObservableCollection<UnknownValue> UnknownValues { get; } = [];

    /// <summary>
    /// 適用で問題が起きた項目。**不一致が 1 件でもあればここに出す**（docs/SPEC.md 9章）。
    /// 書き込めたことと意図した値が入っていることは別である。
    /// </summary>
    public ObservableCollection<string> ApplyIssues { get; } = [];

    /// <summary>
    /// パス欄を畳んでいるときに見出し行へ出す要約。
    ///
    /// **畳んでもどのライブラリを見ているかは残す。** 複数のライブラリを行き来する
    /// ときに、開いている対象が画面から消えると取り違えが起きる。
    /// バックアップ先は並べない。1 行に 2 つ入れると、切り詰めで先に消えるのが
    /// 作業対象のフォルダ名になってしまう。
    /// </summary>
    public string PathSummary => LibraryRoot ?? "ライブラリ未選択";

    /// <summary>
    /// 指定されたライブラリを開いてスキャンする。
    ///
    /// **ライブラリを開く経路はここに集約する。** フォルダ選択・コマンドライン引数・
    /// 前回のライブラリのどれで開いても、次回のために記憶されるようにするため。
    /// </summary>
    /// <param name="libraryRoot">ライブラリのルートパス。</param>
    public async Task OpenAsync(string libraryRoot)
    {
        LibraryRoot = libraryRoot;
        RememberLibraryRoot(libraryRoot);
        await ScanAsync();
    }

    /// <summary>
    /// 起動時に開くライブラリを決めて開く。
    ///
    /// **コマンドライン引数を前回のライブラリより優先する。** 引数はその場の明示的な指示で、
    /// 記憶しているパスは前回の名残にすぎない。
    /// </summary>
    /// <param name="argumentRoot">コマンドラインで渡されたライブラリのパス。渡されていなければ null。</param>
    public async Task StartAsync(string? argumentRoot)
    {
        if (!string.IsNullOrWhiteSpace(argumentRoot))
        {
            if (Directory.Exists(argumentRoot))
            {
                await OpenAsync(argumentRoot);
                return;
            }

            // 引数を指定したのに前回のライブラリが開くと、どちらを見ているのか分からなくなる。
            StatusText = $"指定されたライブラリが見つかりません: {argumentRoot}";
            Log.Warning("引数のライブラリを開けなかった path={Path}", argumentRoot);
            return;
        }

        string? lastRoot = _settingsStore.Current.LastLibraryRoot;

        if (string.IsNullOrWhiteSpace(lastRoot))
        {
            return;
        }

        if (!Directory.Exists(lastRoot))
        {
            // **見つからなくても設定からは消さない。** 外付けドライブを外しているだけかもしれず、
            // 一度きりの不在で忘れると、次に繋いだときに選び直しになる。
            StatusText = $"前回のライブラリが見つかりません: {lastRoot}";
            Log.Information("前回のライブラリを開けなかった path={Path}", lastRoot);
            return;
        }

        await OpenAsync(lastRoot);
    }

    /// <summary>
    /// 折り畳みの状態を設定に残す。
    ///
    /// **保存に失敗しても操作は止めない。** 覚えられないのは次回に開き直す手間だけで、
    /// いま畳めなくする理由にはならない（<see cref="RememberLibraryRoot"/> と同じ判断）。
    /// </summary>
    /// <param name="value">畳んでいるなら true。</param>
    partial void OnArePathsCollapsedChanged(bool value)
    {
        try
        {
            _settingsStore.Save(_settingsStore.Current with { PathsCollapsed = value });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "パス欄の折り畳みを記憶できなかった path={Path}", _settingsStore.FilePath);
        }
    }

    /// <summary>
    /// 次回の起動で開けるよう、ライブラリのパスを設定に残す。
    ///
    /// **保存に失敗しても操作は止めない。** 記憶できないのは次回が不便になるだけで、
    /// いま開いたライブラリを扱えなくなる理由にはならない。
    /// </summary>
    /// <param name="libraryRoot">記憶するライブラリのルートパス。</param>
    private void RememberLibraryRoot(string libraryRoot)
    {
        if (string.Equals(_settingsStore.Current.LastLibraryRoot, libraryRoot, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            _settingsStore.Save(_settingsStore.Current with { LastLibraryRoot = libraryRoot });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "ライブラリを記憶できなかった path={Path}", _settingsStore.FilePath);
        }
    }

    /// <summary>
    /// ライブラリを選んでスキャンする。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartScan))]
    private async Task OpenLibraryAsync()
    {
        OpenFolderDialog dialog = new()
        {
            Title = "ライブラリのフォルダを選択",
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!ConfirmDiscardManualEdits())
        {
            return;
        }

        await OpenAsync(dialog.FolderName);
    }

    /// <summary>
    /// 開いているライブラリを再スキャンする。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRescan))]
    private async Task RescanAsync()
    {
        if (!ConfirmDiscardManualEdits())
        {
            return;
        }

        await ScanAsync();
    }

    /// <summary>
    /// 保留中の手編集を捨ててよいかを確認する。
    ///
    /// 編集は読み取り時点のタグを土台にしているため、読み直すと足場が変わる。
    /// 黙って捨てると、入力した内容が理由も分からず消えたように見える。
    /// </summary>
    /// <returns>続行してよければ true。</returns>
    private bool ConfirmDiscardManualEdits()
    {
        if (!_manualEdits.HasEdits)
        {
            return true;
        }

        return MessageBox.Show(
            string.Create(CultureInfo.CurrentCulture, $"保留中の手編集が {_manualEdits.Count:N0} 項目あります。")
            + Environment.NewLine
            + "読み直すとこの編集は失われます。破棄して続けますか？",
            "保留中の編集があります",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;
    }

    /// <summary>
    /// 実行中のスキャンを中止する。
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsScanning))]
    private void CancelScan()
    {
        _scanCancellation?.Cancel();
        StatusText = "スキャンを中止しています…";
    }

    /// <summary>
    /// バックアップの保存先を選び直す。
    /// </summary>
    [RelayCommand]
    private void ChangeBackupRoot()
    {
        OpenFolderDialog dialog = new()
        {
            Title = "バックアップの保存先を選択",
            Multiselect = false,
        };

        if (!string.IsNullOrWhiteSpace(BackupRoot) && Directory.Exists(BackupRoot))
        {
            dialog.InitialDirectory = BackupRoot;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        ApplyBackupRoot(dialog.FolderName);
    }

    /// <summary>
    /// バックアップの保存先を未指定（ライブラリ直下）に戻す。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanResetBackupRoot))]
    private void ResetBackupRoot()
    {
        ApplyBackupRoot(null);
    }

    /// <summary>保存先が設定されているときだけ「既定に戻す」を押せる。</summary>
    private bool CanResetBackupRoot()
    {
        return !string.IsNullOrWhiteSpace(BackupRoot);
    }

    /// <summary>
    /// 保存先を検証して設定に書き込む。
    ///
    /// **書けない場所を設定に残さない。** 設定した時点では気づかず、
    /// いざバックアップを取る段になって失敗するのが一番困る。
    /// </summary>
    /// <param name="root">新しい保存先。null なら未指定に戻す。</param>
    private void ApplyBackupRoot(string? root)
    {
        if (root is not null)
        {
            string? error = ValidateBackupRoot(root);

            if (error is not null)
            {
                StatusText = error;
                Log.Warning("バックアップ先を採用しなかった path={Path} 理由={Reason}", root, error);
                return;
            }
        }

        try
        {
            _settingsStore.Save(_settingsStore.Current with { BackupRoot = root });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"設定を保存できませんでした: {ex.Message}";
            Log.Error(ex, "設定の保存に失敗した path={Path}", _settingsStore.FilePath);
            return;
        }

        BackupRoot = root;

        // 保存先が増減すると履歴の見え方が変わる。読み直して現状に合わせる。
        RefreshBackups();

        StatusText = root is null
            ? "バックアップ先を既定（ライブラリ直下）に戻しました。"
            : $"バックアップ先を変更しました: {root}";

        Log.Information("バックアップ先を変更した path={Path}", root ?? "(既定)");
    }

    /// <summary>
    /// 保存先として使えるかを確かめる。
    /// </summary>
    /// <param name="root">確かめる保存先。</param>
    /// <returns>使えない理由。使えるなら null。</returns>
    private string? ValidateBackupRoot(string root)
    {
        if (!string.IsNullOrEmpty(LibraryRoot) && IsSameDirectory(root, LibraryRoot))
        {
            return "ライブラリのルートそのものは指定できません（未指定のときと同じ動作になります）。";
        }

        try
        {
            Directory.CreateDirectory(root);

            // 作れることと書けることは別。実際に 1 ファイル置いて確かめる。
            string probePath = Path.Combine(root, WRITE_PROBE_FILE_PREFIX + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);
        }
        catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or ArgumentException
                                       or NotSupportedException)
        {
            return $"このフォルダはバックアップ先に使えません: {ex.Message}";
        }

        return null;
    }

    /// <summary>
    /// 2 つのパスが同じフォルダを指すかを判定する。末尾の区切り文字と大小文字の差は無視する。
    /// </summary>
    private static bool IsSameDirectory(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 現在のライブラリのタグをスナップショットに取る。音声ファイル本体は複製しない。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCreateBackup))]
    private void CreateBackup()
    {
        if (_lastScan is null)
        {
            return;
        }

        try
        {
            string directory = _snapshotService.Create(
                _lastScan,
                SnapshotReason.Manual,
                portableLibraryPath: TagWriter.GetPortableLibraryPath());

            RefreshBackups();

            StatusText = $"バックアップを作成しました: {Path.GetFileName(directory)}"
                + $"（{_lastScan.Tracks.Count:N0} 件）";

            Log.Information("バックアップを作成した directory={Directory} 件数={Count}", directory, _lastScan.Tracks.Count);
        }
        catch (Exception ex)
        {
            StatusText = $"バックアップに失敗しました: {ex.Message}";
            Log.Error(ex, "バックアップに失敗した root={Root}", LibraryRoot);
        }
    }

    /// <summary>
    /// 選択したバックアップとの差分を出す。**復元前に何が戻るのかを確認するための工程。**
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPreviewRestore))]
    private async Task PreviewRestoreAsync()
    {
        if (SelectedBackup is null || string.IsNullOrEmpty(LibraryRoot))
        {
            return;
        }

        IsRestoreReady = false;
        RestoreItems.Clear();

        try
        {
            TagSnapshot snapshot = _snapshotService.Load(SelectedBackup.DirectoryPath);

            // 差分は「今の状態」と比べる必要があるため、その場で読み直す。
            ScanResult current = await _scanner.ScanAsync(LibraryRoot).ConfigureAwait(true);
            _lastScan = current;

            RestorePlan plan = RestoreService.BuildPlan(SelectedBackup.DirectoryPath, snapshot, current);

            foreach (RestoreItem item in plan.Items)
            {
                RestoreItems.Add(item);
            }

            RestoreSummary = plan.Items.Count == 0
                ? "差分はありません。復元する必要はありません。"
                : string.Create(
                    CultureInfo.CurrentCulture,
                    $"{plan.Items.Count:N0} 項目が戻ります"
                    + $"（対象 {plan.Items.Select(item => item.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count():N0} ファイル）。"
                    + $" スナップショット後に消えたファイル {plan.MissingFiles.Count} 件 /"
                    + $" 増えたファイル {plan.AddedFiles.Count} 件は対象外です。");

            IsRestoreReady = plan.Items.Count > 0;
        }
        catch (Exception ex)
        {
            RestoreSummary = $"差分の算出に失敗しました: {ex.Message}";
            Log.Error(ex, "復元計画の作成に失敗した directory={Directory}", SelectedBackup.DirectoryPath);
        }
    }

    /// <summary>
    /// チェックされた項目を書き戻す。復元自体も巻き戻せるよう、直前にスナップショットを取る。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync()
    {
        if (string.IsNullOrEmpty(LibraryRoot) || _lastScan is null)
        {
            return;
        }

        IsScanning = true;
        StatusText = "復元しています…";

        try
        {
            // 復元によって失われる現在の値も残しておく。
            _snapshotService.Create(
                _lastScan,
                SnapshotReason.BeforeRestore,
                note: $"{SelectedBackup?.CreatedAtText} への復元前",
                portableLibraryPath: TagWriter.GetPortableLibraryPath());

            Progress<RestoreProgress> progress = new(report =>
            {
                ProgressMaximum = Math.Max(report.Total, 1);
                ProgressValue = report.Completed;
            });

            RestoreResult result = await _restoreService
                .ApplyAsync(LibraryRoot, [.. RestoreItems], progress)
                .ConfigureAwait(true);

            StatusText = BuildRestoreResultText(result);

            Log.Information(
                "復元完了 対象={Attempted} 成功={Succeeded} 項目={Items} 失敗={Failures} 不一致={Mismatches}",
                result.AttemptedFiles,
                result.SucceededFiles,
                result.RestoredItems,
                result.Failures.Count,
                result.Mismatches.Count);

            foreach (VerificationMismatch mismatch in result.Mismatches)
            {
                Log.Warning(
                    "読み戻し不一致 path={Path} field={Field} expected={Expected} actual={Actual}",
                    mismatch.RelativePath,
                    mismatch.Field,
                    mismatch.Expected,
                    mismatch.Actual);
            }

            IsRestoreReady = false;
            RestoreItems.Clear();
            RefreshBackups();

            await ScanAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = $"復元に失敗しました: {ex.Message}";
            Log.Error(ex, "復元に失敗した root={Root}", LibraryRoot);
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>
    /// 復元結果を 1 行の文言にする。不一致があれば必ず知らせる。
    /// </summary>
    private static string BuildRestoreResultText(RestoreResult result)
    {
        string text = string.Create(
            CultureInfo.CurrentCulture,
            $"復元しました。{result.SucceededFiles:N0} / {result.AttemptedFiles:N0} ファイル、{result.RestoredItems:N0} 項目。");

        if (result.Failures.Count > 0)
        {
            text += $" 失敗 {result.Failures.Count} 件。";
        }

        if (result.Mismatches.Count > 0)
        {
            text += $" ⚠ 読み戻して一致しなかった項目が {result.Mismatches.Count} 件あります（ログを確認してください）。";
        }

        return text;
    }

    /// <summary>
    /// バックアップ履歴を読み直す。
    /// </summary>
    private void RefreshBackups()
    {
        Backups.Clear();

        if (string.IsNullOrEmpty(LibraryRoot))
        {
            return;
        }

        foreach (BackupEntry entry in _snapshotService.List(LibraryRoot))
        {
            Backups.Add(new BackupEntryViewModel(entry));
        }
    }

    /// <summary>
    /// 検査ルールを実行して原則違反を洗い出す。書き込みは行わない。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanInspect))]
    private void Inspect()
    {
        RunInspection();
    }

    /// <summary>
    /// 検査を実行して画面に反映する。
    ///
    /// 辞書を編集した直後にも呼ぶ。**タグは変わっていないので再スキャンは不要**で、
    /// 直近のスキャン結果に新しい索引を当て直すだけでよい。
    /// </summary>
    private void RunInspection()
    {
        if (_lastScan is null)
        {
            return;
        }

        // **Clear より先に控える。** RuleResults を空にすると上段 DataGrid の SelectedItem が
        // null になり、双方向バインド経由で SelectedRule まで消える。控える対象が残らなくなる。
        string? previousRuleId = SelectedRule?.RuleId;
        TagChangeKey? previousChangeKey = SelectedChange is null
            ? null
            : TagChangeKey.From(SelectedChange.Change);

        ClearRuleResults();
        InspectionChanges.Clear();
        UnknownValues.Clear();
        ApplyIssues.Clear();
        CanApplyChanges = false;
        HasInspectionResult = false;
        _lastInspection = null;
        _lastContext = null;

        try
        {
            InspectionOptions options = new()
            {
                EnabledOptionalRuleIds = EnableDiacriticCheck
                    ? new HashSet<string>(StringComparer.Ordinal) { DiacriticMissingRule.RULE_ID }
                    : new HashSet<string>(StringComparer.Ordinal),
            };

            InspectionContext context = new(_lastScan, _dictionaryStore.Index, options);
            InspectionResult result = _inspectionEngine.Inspect(context);

            _lastContext = context;

            foreach (RuleResult rule in result.Results.Where(rule => rule.Changes.Count > 0))
            {
                RuleResultViewModel ruleViewModel = new(rule);
                ruleViewModel.ChangeSelectionChanged += OnInspectionSelectionChanged;
                _allRuleResults.Add(ruleViewModel);
            }

            _lastInspection = result;

            // 表示用のルール行と選択中ルールはここで決まる。絞り込みが無効なら全件が入る。
            // **ルール行は毎回作り直すので、参照では前の選択を追えない。** ID で引き直した実体を
            // 渡せば、ApplyInspectionScope 側の参照一致がそのまま成立する。
            ApplyInspectionScope(FindRule(previousRuleId));

            RestoreSelectedChange(previousChangeKey);

            LoadUnknownValues(result);

            HasInspectionResult = true;
            UpdateInspectionSelection(isDefault: true);

            StatusText = InspectionSummary;

            Log.Information(
                "検査完了 検出={Total} 選択={Selected} 保留={Holds} 未知の値={Unknown} 所要={Elapsed}",
                result.TotalChanges,
                result.AllChanges.Count(change => change.IsSelected),
                result.AllChanges.Count(change => change.HoldReason != HoldReason.None),
                UnknownValues.Count,
                result.Elapsed);
        }
        catch (Exception ex)
        {
            InspectionSummary = $"検査に失敗しました: {ex.Message}";
            Log.Error(ex, "検査に失敗した root={Root}", LibraryRoot);
        }
    }

    /// <summary>
    /// ルール別集計を空にする。**購読を外してから捨てる。**
    ///
    /// <see cref="RunInspection"/> は辞書を編集するたびに呼ばれる。外し忘れると、
    /// 捨てたはずのビューモデルからも通知が届いて集計が多重に走る。
    /// </summary>
    private void ClearRuleResults()
    {
        foreach (RuleResultViewModel rule in _allRuleResults)
        {
            rule.ChangeSelectionChanged -= OnInspectionSelectionChanged;
        }

        _allRuleResults.Clear();
        RuleResults.Clear();
    }

    /// <summary>
    /// ルール ID から、作り直したあとのルール行を引く。
    ///
    /// 再検査は <see cref="RuleResultViewModel"/> を毎回新しく作るため、参照を持ち越しても
    /// 一致しない。**キーで引き直す**（<c>DictionaryViewModel.Reveal</c> と同じ作法）。
    /// </summary>
    /// <param name="ruleId">探すルール ID。控えていなければ null。</param>
    /// <returns>見つかったルール行。無ければ null。</returns>
    private RuleResultViewModel? FindRule(string? ruleId)
    {
        return ruleId is null
            ? null
            : _allRuleResults.FirstOrDefault(rule => string.Equals(rule.RuleId, ruleId, StringComparison.Ordinal));
    }

    /// <summary>
    /// 再検査の前に選んでいた明細を選び直す。
    ///
    /// **見つからなければ選択なしのままにする。** 対象外にしたアルバムの明細は一覧から消えるのが
    /// 正しい挙動で、消えた行の代わりに別の行を選ぶと、直したつもりのない行を直すことになる。
    /// </summary>
    /// <param name="key">再検査の前に選んでいた明細のキー。無ければ null。</param>
    private void RestoreSelectedChange(TagChangeKey? key)
    {
        if (key is null || SelectedChange is not null)
        {
            return;
        }

        SelectedChange = InspectionChanges
            .FirstOrDefault(change => TagChangeKey.From(change.Change) == key.Value);
    }

    /// <summary>
    /// 明細のチェックが変わったので、選択件数と適用の可否を取り直す。
    ///
    /// **ここでコレクションを触らないこと。** 呼ばれるのは DataGrid がセルの編集
    /// トランザクションを開いている最中で、その状態で
    /// <see cref="System.ComponentModel.ICollectionView.Refresh"/> を呼ぶと落ちる
    /// (<see cref="GridViewRefresher"/>)。数えて表示を書き換えるだけに留める。
    /// </summary>
    private void OnInspectionSelectionChanged(object? sender, EventArgs e)
    {
        UpdateInspectionSelection(isDefault: false);

        // 「チェック済みのみ」で絞っているあいだは、チェックを外した行が対象から外れる。
        // 掛け直しはビューに任せられる（編集中なら GridViewRefresher が見送り、
        // NotifyInspectionEditFinished で掛け直す）。コレクション自体は触らない。
        //
        // **絞っていないときは掛け直さない。** 絞り込みの結果は変わらないのに、
        // チェックを 1 つ付け外しするたび下段の現在行とスクロール位置が飛ぶ。
        if (ShowOnlySelectedChanges)
        {
            _inspectionChangeRefresher?.Request();
        }
    }

    /// <summary>
    /// 差分明細のセル編集が終わったので、見送っていた絞り込みを掛け直す。
    ///
    /// **チェックボックスのクリックは編集トランザクションを開く。** その最中は絞り直せず、
    /// 掛け直しは <see cref="GridViewRefresher"/> が持ち越している。View から呼ぶ。
    /// </summary>
    public void NotifyInspectionEditFinished()
    {
        _inspectionChangeRefresher?.Resume();
    }

    /// <summary>
    /// 選択件数から要約テキストと適用の可否を作り直す。
    /// </summary>
    /// <param name="isDefault">
    /// 検査直後の既定値そのままか。既定値であることは利用者が知るべき情報なので
    /// （docs/SPEC.md 9.1）文言で区別する。利用者が触ったあとは所要時間も再掲しない。
    /// </param>
    private void UpdateInspectionSelection(bool isDefault)
    {
        if (_lastInspection is null)
        {
            return;
        }

        // 適用対象の唯一の真実は Core 側にある。ビューモデルは書き込みを素通しするだけなので、
        // ここは検査結果をそのまま数えればよい。
        // **数える範囲は画面に出している範囲に揃える。** 見えていない項目を件数に混ぜると、
        // 「選択 N 件」と実際に書き込まれる件数が食い違う。
        TagChange[] scoped = [.. ScopedChanges()];
        int selected = scoped.Count(change => change.IsSelected);
        int holds = scoped.Count(change => change.HoldReason != HoldReason.None);

        // 絞り込み中は対象を明示する。件数だけ減っていると、検査し直したのか絞ったのか読めない。
        string scopeSuffix = InspectionScopeLabel is { Length: > 0 } scopeName
            ? $"（{scopeName} 配下）"
            : string.Empty;

        InspectionSummary = isDefault
            ? string.Create(
                CultureInfo.CurrentCulture,
                $"検出 {scoped.Length:N0} 件 / 既定で選択 {selected:N0} 件 / 保留 {holds:N0} 件"
                + $"（{_lastInspection.Elapsed.TotalSeconds:F2} 秒）")
                + scopeSuffix
            : string.Create(
                CultureInfo.CurrentCulture,
                $"検出 {scoped.Length:N0} 件 / 選択 {selected:N0} 件 / 保留 {holds:N0} 件")
                + scopeSuffix;

        CanApplyChanges = selected > 0;
    }

    /// <summary>上段（ルール別集計）の全項目を選択する。</summary>
    [RelayCommand(CanExecute = nameof(CanBulkSelectRuleChanges))]
    private void SelectAllRuleChanges()
    {
        UpdateAllRuleSelections(_ => true);
    }

    /// <summary>上段（ルール別集計）の全項目を選択解除する。</summary>
    [RelayCommand(CanExecute = nameof(CanBulkSelectRuleChanges))]
    private void DeselectAllRuleChanges()
    {
        UpdateAllRuleSelections(_ => false);
    }

    /// <summary>
    /// 上段配下の全項目のチェックを個別に反転する。
    ///
    /// ルール行のヘッダーチェックを一括トグルするのではなく、明細
    /// （<see cref="TagChange.IsSelected"/>）を 1 件ずつ反転する。ヘッダーは
    /// 反転後の配下の実態から決め直される（<see cref="RuleResultViewModel"/>）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanBulkSelectRuleChanges))]
    private void InvertRuleChanges()
    {
        UpdateAllRuleSelections(selected => !selected);
    }

    /// <summary>上段の一括選択操作を実行できるか。修正案を持つ明細が 1 件も無ければ押させない。</summary>
    private bool CanBulkSelectRuleChanges()
    {
        return RuleResults.Any(rule => rule.FixableCount > 0);
    }

    /// <summary>下段（選択中ルールの差分明細）の全項目を選択する。</summary>
    [RelayCommand(CanExecute = nameof(CanBulkSelectChanges))]
    private void SelectAllChanges()
    {
        SelectedRule?.UpdateChangeSelection(_ => true);
    }

    /// <summary>下段（選択中ルールの差分明細）の全項目を選択解除する。</summary>
    [RelayCommand(CanExecute = nameof(CanBulkSelectChanges))]
    private void DeselectAllChanges()
    {
        SelectedRule?.UpdateChangeSelection(_ => false);
    }

    /// <summary>下段（選択中ルールの差分明細）のチェックを個別に反転する。</summary>
    [RelayCommand(CanExecute = nameof(CanBulkSelectChanges))]
    private void InvertChanges()
    {
        SelectedRule?.UpdateChangeSelection(selected => !selected);
    }

    /// <summary>下段の一括選択操作を実行できるか。修正案を持つ明細が 1 件も無ければ押させない。</summary>
    private bool CanBulkSelectChanges()
    {
        return SelectedRule is not null && SelectedRule.FixableCount > 0;
    }

    /// <summary>
    /// 全ルール配下のチェックをまとめて書き換える。
    /// </summary>
    /// <param name="next">今のチェック状態から次の状態を決める。</param>
    private void UpdateAllRuleSelections(Func<bool, bool> next)
    {
        foreach (RuleResultViewModel rule in RuleResults)
        {
            rule.UpdateChangeSelection(next);
        }
    }

    /// <summary>
    /// 未知の値の一覧を作り直す。
    /// </summary>
    private void LoadUnknownValues(InspectionResult result)
    {
        foreach (UnknownValue unknown in UnknownValueCollector.Collect(result.AllChanges))
        {
            UnknownValues.Add(unknown);
        }

        SelectedUnknownValue = UnknownValues.FirstOrDefault();

        int fileCount = UnknownValues.Sum(unknown => unknown.Count);

        UnknownValueSummary = UnknownValues.Count == 0
            ? "辞書に無い値はありません。"
            : string.Create(
                CultureInfo.CurrentCulture,
                $"辞書に無い値 {UnknownValues.Count:N0} 種 / {fileCount:N0} ファイル。")
                + " 行を選んで「辞書に追加」を押すと、登録して再検査します。";
    }

    /// <summary>
    /// 辞書が保存されたら検査をやり直す。
    /// 古い検査結果を残すと、辞書に足したのにまだ未知の値として出ているように見える。
    /// </summary>
    private void OnDictionarySaved(object? sender, EventArgs e)
    {
        // 候補は検査結果と無関係に作れる。スキャン前でも辞書に足した名前は選べるようにする。
        RefreshSuggestions();

        if (_lastScan is null)
        {
            return;
        }

        RunInspection();
        StatusText = "辞書の保存にあわせて検査をやり直しました。 " + InspectionSummary;
    }

    /// <summary>
    /// 未知の値を辞書に足して再検査する。段階 5 の中心になる導線（docs/SPEC.md 7.3）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddUnknownValueToDictionary))]
    private void AddUnknownValueToDictionary()
    {
        if (SelectedUnknownValue is not null)
        {
            AddToDictionary(SelectedUnknownValue);
        }
    }

    /// <summary>
    /// 差分明細で選んだ行の値を辞書に足す。
    /// 明細を見ている流れのまま登録に進めるようにするための入口。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddSelectedChangeToDictionary))]
    private void AddSelectedChangeToDictionary()
    {
        if (SelectedChange is null)
        {
            return;
        }

        UnknownValue unknown = UnknownValueCollector.Collect([SelectedChange.Change]).FirstOrDefault()
            ?? new UnknownValue(
                SelectedChange.BeforeText,
                DictionaryEditor.SuggestCategory(SelectedChange.Field),
                1,
                [SelectedChange.Field],
                SelectedChange.RelativePath,
                [SelectedChange.RuleId]);

        AddToDictionary(unknown);
    }

    /// <summary>
    /// 選択中の保留行から作品を辞書に足す（docs/SPEC.md 7.3.2）。
    ///
    /// **これが作品エントリを育てる主経路になる。** 辞書タブで作曲家名を手打ちさせない。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddWorkFromChange))]
    private void AddWorkFromChange()
    {
        if (SelectedChange is null || FindUnit(SelectedChange) is not AlbumUnit unit || unit.Composers.Count != 1)
        {
            return;
        }

        if (!Dictionary.ConfirmDiscardIfDirty())
        {
            return;
        }

        AddWorkViewModel viewModel = new(_dictionaryStore.Dictionary, _dictionaryStore.Index, unit, unit.Composers[0]);

        AddWorkWindow window = new(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        SaveDictionary(
            viewModel.Apply,
            $"作品「{viewModel.Canonical.Trim()}」を辞書に登録して再検査しました。",
            () => Log.Information(
                "作品を辞書に追加した composer={Composer} canonical={Canonical} folder={Folder} disc={Disc}",
                unit.Composers[0],
                viewModel.Canonical.Trim(),
                unit.Folder,
                unit.Disc));
    }

    /// <summary>
    /// 選択中の行が指すアルバム単位に個別例外を足す（docs/SPEC.md 7.3.2 / 7.4.5）。
    ///
    /// フォルダと <c>disc</c> は明細から自動で埋める。**手で相対パスを打たせない。**
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddAlbumOverrideFromChange))]
    private void AddAlbumOverrideFromChange()
    {
        if (SelectedChange is null || FindUnit(SelectedChange) is not AlbumUnit unit)
        {
            return;
        }

        if (!Dictionary.ConfirmDiscardIfDirty())
        {
            return;
        }

        // 保留の種類をダイアログへ渡す。年の割れで開いたときは対象外を選ばせない（7.4.4）。
        AlbumOverrideViewModel viewModel = new(
            _dictionaryStore.Dictionary, unit, SelectedChange.Change.HoldReason);

        AlbumOverrideWindow window = new(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        AlbumOverrideEntry entry = viewModel.BuildEntry();

        // 対象外にした単位は検査結果から消える（7.4.3 手順1）。消えたことが分かるように書く。
        string message = entry.Exclude
            ? $"「{FolderLabel(unit)}」を対象外にしました。この単位は検査結果から消えます。"
            : $"「{FolderLabel(unit)}」に個別例外を登録して再検査しました。";

        SaveDictionary(
            viewModel.Apply,
            message,
            () => Log.Information(
                "個別例外を辞書に追加した folder={Folder} disc={Disc} exclude={Exclude} composer={Composer} work={Work} date={Date}",
                entry.Folder,
                entry.Disc,
                entry.Exclude,
                entry.Composer,
                entry.WorkName,
                entry.Date));
    }

    /// <summary>
    /// 明細の行が属するアルバム単位を探す（フォルダ + <c>discnumber</c>。3.5 補足2）。
    /// </summary>
    /// <param name="change">明細の行。</param>
    /// <returns>該当する単位。見つからなければ null。</returns>
    private AlbumUnit? FindUnit(TagChangeViewModel change)
    {
        if (_lastContext is null || _lastScan is null)
        {
            return null;
        }

        TrackTags? track = _lastScan.Tracks.FirstOrDefault(
            candidate => string.Equals(candidate.RelativePath, change.RelativePath, StringComparison.OrdinalIgnoreCase));

        if (track is null)
        {
            return null;
        }

        string folder = InspectionContext.GetFolder(track.RelativePath);
        int disc = AlbumUnit.GetDisc(track.DiscNumber);

        return _lastContext.Units.FirstOrDefault(
            unit => string.Equals(unit.Folder, folder, StringComparison.Ordinal) && unit.Disc == disc);
    }

    /// <summary>
    /// 単位を 1 行で表す。ルート直下は空文字になるので、そのままだと何を指すのか分からない。
    /// </summary>
    private static string FolderLabel(AlbumUnit unit)
    {
        return unit.Folder.Length == 0 ? "(ルート直下)" : unit.Folder;
    }

    /// <summary>
    /// 辞書を更新して保存し、再検査する。検証でエラーが出た場合は保存しない。
    /// </summary>
    /// <param name="edit">更新後の辞書を作る。</param>
    /// <param name="message">成功時にステータスへ出す文言。</param>
    /// <param name="log">成功時に残すログ。</param>
    private void SaveDictionary(Func<TagDictionary> edit, string message, Action log)
    {
        try
        {
            TagDictionary edited = edit();

            IReadOnlyList<DictionaryIssue> issues = DictionaryValidator.Validate(edited);

            if (DictionaryValidator.HasError(issues))
            {
                // 索引に載らない登録をさせない。理由を出して中止する。
                MessageBox.Show(
                    "この内容では辞書に登録できません。"
                    + Environment.NewLine + Environment.NewLine
                    + string.Join(
                        Environment.NewLine,
                        issues.Where(issue => issue.Severity == DictionaryIssueSeverity.Error).Select(issue => issue.Summary)),
                    "辞書に登録できません",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            _dictionaryStore.Save(edited);
            Dictionary.ReloadFromStore();

            log();

            RunInspection();

            StatusText = $"{message} {InspectionSummary}";
        }
        catch (Exception ex)
        {
            StatusText = $"辞書への追加に失敗しました: {ex.Message}";
            Log.Error(ex, "辞書への追加に失敗した");
        }
    }

    /// <summary>
    /// 追加ダイアログを開き、確定したら辞書を保存して再検査する。
    /// </summary>
    private void AddToDictionary(UnknownValue unknown)
    {
        if (!Dictionary.ConfirmDiscardIfDirty())
        {
            return;
        }

        AddToDictionaryViewModel viewModel = new(_dictionaryStore.Dictionary, _dictionaryStore.Index, unknown);

        AddToDictionaryWindow window = new(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        SaveDictionary(
            viewModel.Apply,
            $"「{unknown.Value}」を辞書に登録して再検査しました。",
            () => Log.Information(
                "辞書に追加した value={Value} category={Category} 件数={Count}",
                unknown.Value,
                unknown.Category,
                unknown.Count));
    }

    /// <summary>
    /// 検査結果を CSV に書き出す（docs/SPEC.md 5.1）。
    ///
    /// 明細と集計の 2 ファイルを出す。集計は全体像を先に掴むため、明細は 1 行ずつ確認するため。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExportCsv))]
    private void ExportCsv()
    {
        if (_lastInspection is null)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "検査結果を CSV に書き出す",
            FileName = $"{AppConst.CHANGE_CSV_FILE_NAME_PREFIX}{DateTime.Now:yyyyMMddHHmmss}.csv",
            Filter = "CSV ファイル|*.csv",
            DefaultExt = ".csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            string summaryPath = BuildSummaryPath(dialog.FileName);

            // 画面と同じ範囲を書き出す。表と CSV で件数が違うと、どちらが本当か確かめられない。
            TagChange[] exported = [.. ScopedChanges()];
            string scopeSuffix = InspectionScopeLabel is { Length: > 0 } scopeName
                ? $" / {scopeName} 配下"
                : string.Empty;

            ChangeCsvExporter.WriteFile(dialog.FileName, exported);
            ChangeCsvExporter.WriteSummaryFile(summaryPath, exported);

            StatusText = string.Create(
                CultureInfo.CurrentCulture,
                $"CSV を書き出しました（{exported.Length:N0} 件{scopeSuffix}）: ")
                + $"{Path.GetFileName(dialog.FileName)} / {Path.GetFileName(summaryPath)}";

            Log.Information(
                "CSV を書き出した path={Path} summary={Summary} 件数={Count} 範囲={Scope}",
                dialog.FileName,
                summaryPath,
                exported.Length,
                InspectionScopeLabel.Length == 0 ? "(全体)" : InspectionScopeLabel);
        }
        catch (Exception ex)
        {
            StatusText = $"CSV の書き出しに失敗しました: {ex.Message}";
            Log.Error(ex, "CSV の書き出しに失敗した path={Path}", dialog.FileName);
        }
    }

    /// <summary>
    /// いま一覧に出ている行を返す。**CSV に書き出す範囲そのもの。**
    ///
    /// ツリーのフォルダ選択（<see cref="Tracks"/> への詰め替え）と、検索文字列・
    /// 「空欄のある行のみ」・「編集した行のみ」（<see cref="MatchesTrackFilter"/>）の
    /// 両方が効いた結果になる。並べ替えも一覧のビューに従う。
    /// </summary>
    /// <returns>表示順に並んだ行。</returns>
    public IReadOnlyList<TrackRowViewModel> VisibleTracks()
    {
        // ビューはファイル一覧タブを一度でも組み立てれば必ず在る。無い間は絞り込みも無い。
        return _trackView is null
            ? [.. Tracks]
            : [.. _trackView.Cast<TrackRowViewModel>()];
    }

    /// <summary>
    /// ファイル一覧を CSV に書き出す（docs/SPEC.md 5.2）。
    ///
    /// **書き出すのは画面に出ている行だけ。** 絞り込みで隠した行まで出ると、
    /// 表と CSV のどちらが本当か確かめられない（検査結果の CSV 出力と同じ考え方）。
    /// 値は保留中の手編集を反映したもの＝セルに見えているとおりになる。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExportTrackCsv))]
    private void ExportTrackCsv()
    {
        SaveFileDialog dialog = new()
        {
            Title = "ファイル一覧を CSV に書き出す",
            FileName = $"{AppConst.TRACK_CSV_FILE_NAME_PREFIX}{DateTime.Now:yyyyMMddHHmmss}.csv",
            Filter = "CSV ファイル|*.csv",
            DefaultExt = ".csv",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            IReadOnlyList<TrackRowViewModel> exported = VisibleTracks();

            TrackCsvExporter.WriteFile(dialog.FileName, exported.Select(row => row.Tags), _manualEdits);

            // 全件数を併記する。件数だけでは、絞り込んだ結果なのか読み取れていないのかが判らない。
            StatusText = string.Create(
                CultureInfo.CurrentCulture,
                $"CSV を書き出しました（{exported.Count:N0} 件 / 全 {_allTracks.Count:N0} 件）: ")
                + Path.GetFileName(dialog.FileName);

            Log.Information(
                "ファイル一覧を CSV に書き出した path={Path} 件数={Count} 全件={Total}",
                dialog.FileName,
                exported.Count,
                _allTracks.Count);
        }
        catch (Exception ex)
        {
            StatusText = $"CSV の書き出しに失敗しました: {ex.Message}";
            Log.Error(ex, "ファイル一覧の CSV 書き出しに失敗した path={Path}", dialog.FileName);
        }
    }

    /// <summary>ファイル一覧を書き出せるか。</summary>
    private bool CanExportTrackCsv()
    {
        return HasTracks;
    }

    /// <summary>
    /// 集計 CSV のパスを組み立てる。明細と並べて置く。
    /// </summary>
    private static string BuildSummaryPath(string detailPath)
    {
        string directory = Path.GetDirectoryName(detailPath) ?? string.Empty;

        return Path.Combine(
            directory,
            Path.GetFileNameWithoutExtension(detailPath) + "-summary" + Path.GetExtension(detailPath));
    }

    /// <summary>未知の値を辞書に足せるか。</summary>
    private bool CanAddUnknownValueToDictionary()
    {
        return !IsScanning && SelectedUnknownValue is not null;
    }

    /// <summary>選択中の明細を辞書に足せるか。</summary>
    private bool CanAddSelectedChangeToDictionary()
    {
        return !IsScanning && SelectedChange is not null && !SelectedChange.HasFix;
    }

    /// <summary>
    /// 選択中の行から作品を足せるか（docs/SPEC.md 7.3.2）。
    ///
    /// **単位内に作曲家が複数ある場合は出さない。** 作品を足しても保留は解けず、
    /// 主作品を決められるかどうかは機械には分からない（3.5 規則5・規則6）。
    /// そちらは「このアルバムの扱いを決める」で扱う。
    /// </summary>
    private bool CanAddWorkFromChange()
    {
        return !IsScanning
            && SelectedChange is { RuleId: AlbumNameRule.RULE_ID } change
            && change.Change.HoldReason == HoldReason.WorkUnknown
            && FindUnit(change) is { Composers.Count: 1 };
    }

    /// <summary>
    /// 選択中の行から個別例外を足せるか（docs/SPEC.md 7.3.2）。
    ///
    /// 対象は R-504 の <see cref="HoldReason.WorkUnknown"/>・<see cref="HoldReason.DateUnknown"/> と
    /// R-501 の明細。作品が定まらない単位には作曲家・作品名を、年が割れている単位には主作品の年を
    /// 書いて解く（3.5 規則2・規則5・規則6）。
    ///
    /// **<c>artist</c> の保留では出さない。** 個別例外に <c>artist</c> は無いので**書いても
    /// 解けない**のに、対象外にすれば一覧からは消える。タグが割れたまま検出だけ消えた単位が
    /// できる（SPEC 7.4.4）。そちらはファイル一覧タブでタグを直すか、フォルダを分けて解く。
    ///
    /// 年の保留から開いた場合、ダイアログ側で対象外を選べなくする（<see cref="AlbumOverrideViewModel.CanExclude"/>）。
    /// ここまで来た単位は作品が決まっている＝主作品が定まっているので、規則6 には当たらない。
    /// </summary>
    private bool CanAddAlbumOverrideFromChange()
    {
        if (IsScanning || SelectedChange is null || FindUnit(SelectedChange) is not AlbumUnit unit)
        {
            return false;
        }

        if (SelectedChange.RuleId == AlbumNameCollisionRule.RULE_ID)
        {
            return true;
        }

        if (SelectedChange.RuleId != AlbumNameRule.RULE_ID)
        {
            return false;
        }

        return SelectedChange.Change.HoldReason switch
        {
            HoldReason.WorkUnknown => true,

            // 年に書けるのは**単位内にある値のどれを採るか**だけ。未設定の単位には選ぶものが無く、
            // 開いても何もできない。そちらは CD 実物を確かめてタグに入れる以外に道がない。
            HoldReason.DateUnknown => unit.Dates.Count > 1,

            _ => false,
        };
    }

    /// <summary>CSV を書き出せるか。</summary>
    private bool CanExportCsv()
    {
        return _lastInspection is not null;
    }

    /// <summary>
    /// 選択した行に同じ値を入れる（docs/SPEC.md 5.2）。
    /// **アルバム単位の編集で必須**とされている操作。
    /// </summary>
    /// <param name="selection">一覧で選択されている行。</param>
    [RelayCommand]
    private void BulkInput(System.Collections.IList? selection)
    {
        TrackRowViewModel[] rows = [.. (selection ?? Array.Empty<object>()).OfType<TrackRowViewModel>()];

        if (rows.Length == 0)
        {
            StatusText = "一括入力する行を選んでください。";
            return;
        }

        // 値を消す一括入力は取り返しが付きにくいので、件数を示して確認する。
        if (BulkValue.Trim().Length == 0 && !ConfirmBulkClear(rows.Length))
        {
            return;
        }

        int applied = _manualEdits.SetMany(rows.Select(row => row.Tags), BulkField, BulkValue);

        foreach (TrackRowViewModel row in rows)
        {
            row.NotifyEditsChanged();
        }

        StatusText = string.Create(
            CultureInfo.CurrentCulture,
            $"{ManualEditConst.Label(BulkField)} に一括入力しました（{applied:N0} / {rows.Length:N0} 行）。")
            + " 変更が無かった行は編集になりません。";
    }

    /// <summary>
    /// 一括入力の対象フィールドが変わったら、出す候補を入れ替える。
    /// </summary>
    partial void OnBulkFieldChanged(TagField value)
    {
        RefreshBulkSuggestions();
    }

    /// <summary>
    /// 入力欄に出す候補を作り直す。
    ///
    /// **索引と同じく握り込まない。** 辞書は編集できるので、保存のたびに作り直さないと
    /// 辞書に足した名前がいつまでも候補に出てこない。
    /// </summary>
    private void RefreshSuggestions()
    {
        TagDictionary dictionary = _dictionaryStore.Dictionary;

        ComposerSuggestions = DictionarySuggester.BuildCandidates(dictionary, DictionaryCategory.Composer);
        PersonSuggestions = DictionarySuggester.BuildCandidates(dictionary, DictionaryCategory.Person);
        EnsembleSuggestions = DictionarySuggester.BuildCandidates(dictionary, DictionaryCategory.Ensemble);

        OnPropertyChanged(nameof(ComposerSuggestions));
        OnPropertyChanged(nameof(PersonSuggestions));
        OnPropertyChanged(nameof(EnsembleSuggestions));

        RefreshBulkSuggestions();
    }

    /// <summary>
    /// 一括入力の欄に出す候補を、対象フィールドに合わせて選び直す。
    /// 辞書が扱わないフィールド（曲名・年など）では候補を出さない。
    /// </summary>
    private void RefreshBulkSuggestions()
    {
        BulkSuggestions = DictionarySuggester.CategoryFor(BulkField) switch
        {
            DictionaryCategory.Composer => ComposerSuggestions,
            DictionaryCategory.Person => PersonSuggestions,
            DictionaryCategory.Ensemble => EnsembleSuggestions,
            _ => [],
        };

        OnPropertyChanged(nameof(BulkSuggestions));
    }

    /// <summary>
    /// 一括で値を消してよいかを確認する。
    /// </summary>
    private static bool ConfirmBulkClear(int rowCount)
    {
        return MessageBox.Show(
            string.Create(CultureInfo.CurrentCulture, $"{rowCount:N0} 行のタグを空にします。")
            + Environment.NewLine + Environment.NewLine
            + "空欄にすること自体は原則が認める操作ですが（TAGGING_POLICY 7.4）、"
            + "入力し忘れでないかを確認してください。",
            "選択した行のタグを空にしますか？",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;
    }

    /// <summary>
    /// 保留中の手編集をすべて捨てる。
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasManualEdits))]
    private void DiscardManualEdits()
    {
        bool confirmed = MessageBox.Show(
            string.Create(CultureInfo.CurrentCulture, $"保留中の編集 {_manualEdits.Count:N0} 項目を捨てます。"),
            "編集を破棄しますか？",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;

        if (!confirmed)
        {
            return;
        }

        _manualEdits.Clear();
        RefreshTrackRows();

        StatusText = "保留中の編集を破棄しました。";
    }

    /// <summary>
    /// 保留中の手編集を 1 件だけ取り消す（下段の一覧の右クリック）。
    ///
    /// **間違えて 1 セル直してしまったときの戻り道。** これが無いと、誤入力を消す手段は
    /// 全件破棄か、元の値を思い出して打ち直すかしか無い。
    ///
    /// **確認は出さない。** 取り消すのは右クリックした行そのもので、対象が画面に見えている。
    /// 全件破棄と違い、取り違えても失うのは 1 項目だけで、打ち直せば戻せる。
    /// </summary>
    /// <param name="change">取り消す差分。null なら何もしない。</param>
    [RelayCommand]
    private void DiscardManualEdit(TagChange? change)
    {
        if (change is null || !_manualEdits.Remove(change.RelativePath, change.Field))
        {
            return;
        }

        // 消えたのは 1 行 1 フィールドだけなので、一覧も該当行だけ出し直す。
        FindTrackRow(change.RelativePath)?.NotifyEditsChanged();

        StatusText = $"「{Path.GetFileName(change.RelativePath)}」の{ManualEditConst.Label(change.Field)}の編集を取り消しました。";
    }

    /// <summary>
    /// ある行の保留中の手編集をまとめて取り消す（ファイル一覧の右クリック）。
    ///
    /// 1 行を直しているうちに何項目も入ってしまうことがあり、下段から 1 件ずつ消すのは手間が勝つ。
    /// 確認を出さない理由は <see cref="DiscardManualEdit"/> と同じ。
    /// </summary>
    /// <param name="row">対象の行。null なら何もしない。</param>
    [RelayCommand(CanExecute = nameof(CanResetTrackEdits))]
    private void ResetTrackEdits(TrackRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        int removed = _manualEdits.Reset(row.RelativePath);

        if (removed == 0)
        {
            return;
        }

        row.NotifyEditsChanged();

        StatusText = string.Create(
            CultureInfo.CurrentCulture,
            $"「{row.FileName}」の編集 {removed:N0} 項目を取り消しました。");
    }

    /// <summary>
    /// その行の編集を取り消せるか。編集が無い行では押せないようにする。
    /// </summary>
    /// <param name="row">対象の行。</param>
    /// <returns>取り消せるなら true。</returns>
    private static bool CanResetTrackEdits(TrackRowViewModel? row)
    {
        return row is { IsEdited: true };
    }

    /// <summary>
    /// 相対パスからファイル一覧の行を探す。
    /// </summary>
    /// <param name="relativePath">対象ファイル。</param>
    /// <returns>見つかった行。無ければ null。</returns>
    private TrackRowViewModel? FindTrackRow(string relativePath)
    {
        return _allTracks
            .FirstOrDefault(track => track.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 保留中の手編集を書き込む。
    ///
    /// 検査結果の適用とまったく同じ経路を通す。書き込み経路を 2 本持つと、
    /// 自動バックアップや読み戻し照合を片方で入れ忘れる。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApplyManualEdits))]
    private async Task ApplyManualEditsAsync()
    {
        if (_lastScan is null)
        {
            return;
        }

        TagChange[] targets = [.. _manualEdits.ToChanges().Where(change => change.IsSelected && change.HasFix)];

        if (targets.Length == 0)
        {
            StatusText = "適用する編集がありません。";
            return;
        }

        if (!ConfirmApplyManualEdits(targets))
        {
            return;
        }

        IsScanning = true;
        ApplyIssues.Clear();
        ProgressValue = 0;
        ProgressMaximum = 1;
        StatusText = "手編集を適用しています…";

        try
        {
            Progress<ApplyProgress> progress = new(report =>
            {
                ProgressMaximum = Math.Max(report.Total, 1);
                ProgressValue = report.Completed;
            });

            ApplyResult result = await _applyService
                .ApplyAsync(
                    _lastScan,
                    targets,
                    note: $"手編集の適用前（{targets.Length} 項目）",
                    portableLibraryPath: TagWriter.GetPortableLibraryPath(),
                    progress: progress)
                .ConfigureAwait(true);

            ShowApplyResult(result);

            Log.Information("手編集を適用した 項目={Count}", targets.Length);

            // 書き込みが済んだので保留分は役目を終える。残すと二重に適用しかねない。
            _manualEdits.Clear();
        }
        catch (Exception ex)
        {
            StatusText = $"手編集の適用に失敗しました: {ex.Message}";
            Log.Error(ex, "手編集の適用に失敗した root={Root}", LibraryRoot);
        }
        finally
        {
            IsScanning = false;
        }

        await ScanAsync().ConfigureAwait(true);
        RefreshBackups();
    }

    /// <summary>
    /// 手編集を適用してよいかを確認する。
    /// </summary>
    private bool ConfirmApplyManualEdits(IReadOnlyList<TagChange> targets)
    {
        int fileCount = targets
            .Select(change => change.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        string breakdown = string.Join(
            Environment.NewLine,
            targets.GroupBy(change => change.Field)
                .OrderBy(group => group.Key)
                .Select(group => $"  {ManualEditConst.Label(group.Key)}  {group.Count():N0} 項目"));

        string warningText = ManualEditWarnings.Count == 0
            ? string.Empty
            : Environment.NewLine + Environment.NewLine
                + $"⚠ 気づいてほしい点が {ManualEditWarnings.Count} 件あります（下の一覧を確認してください）。";

        string message = string.Create(
            CultureInfo.CurrentCulture,
            $"{fileCount:N0} ファイルに {targets.Count:N0} 項目を書き込みます。")
            + Environment.NewLine + Environment.NewLine
            + breakdown
            + warningText
            + Environment.NewLine + Environment.NewLine
            + "書き込みの直前にタグのスナップショットを自動で取ります。"
            + Environment.NewLine
            + "適用後は全項目を読み戻して照合します。";

        return MessageBox.Show(
            message,
            "手編集を適用しますか？",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;
    }

    /// <summary>手編集を適用できるか。</summary>
    private bool CanApplyManualEdits()
    {
        return !IsScanning && HasManualEdits;
    }

    /// <summary>
    /// 手編集が変わったら差分と警告を作り直す。
    /// </summary>
    private void OnManualEditsChanged(object? sender, EventArgs e)
    {
        ManualEditChanges.Clear();
        ManualEditWarnings.Clear();

        IReadOnlyList<TagChange> changes = _manualEdits.ToChanges();

        foreach (TagChange change in changes)
        {
            ManualEditChanges.Add(change);
        }

        if (_lastScan is not null)
        {
            foreach (ManualEditWarning warning in
                ManualEditValidator.Validate(changes, _lastScan.Tracks, _dictionaryStore.Index))
            {
                ManualEditWarnings.Add(warning);
            }
        }

        HasManualEdits = changes.Count > 0;

        // 行の取り消しの可否は行ごとに変わる。**ここで知らせないと押せないままになる。**
        // CommunityToolkit の RelayCommand は CommandManager の再問い合わせに乗らないため、
        // 同じ行を右クリックし直しても CanExecute は評価し直されない。
        ResetTrackEditsCommand.NotifyCanExecuteChanged();

        ManualEditSummary = changes.Count == 0
            ? "セルを直すと、ここに保留中の編集が集まります。"
            : string.Create(
                CultureInfo.CurrentCulture,
                $"保留中の編集 {changes.Count:N0} 項目 /"
                + $" {changes.Select(change => change.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count():N0} ファイル。"
                + $" 気づいてほしい点 {ManualEditWarnings.Count:N0} 件。");

        // 編集済みだけを表示している場合、絞り込みの結果が変わる。
        _trackRefresher?.Request();
    }

    /// <summary>
    /// 一覧の行の表示を出し直す。一括入力や編集の破棄のあとに呼ぶ。
    /// </summary>
    private void RefreshTrackRows()
    {
        foreach (TrackRowViewModel row in _allTracks)
        {
            row.NotifyEditsChanged();
        }

        _trackRefresher?.Request();
    }

    /// <summary>
    /// ファイル一覧の行の編集が終わったことを受けて、見送っていた絞り込みを掛け直す。
    ///
    /// **編集トランザクションが閉じてから呼ぶ。** DataGrid の RowEditEnding は確定処理の
    /// 前に上がるため、そこから直に呼んでも見送られるだけになる。入力イベントを抜けてから
    /// 呼ぶ段取りは View が持つ（<see cref="TrackRevealRequested"/> と同じ役割分担）。
    /// </summary>
    public void NotifyTrackEditFinished()
    {
        _trackRefresher?.Resume();
    }

    /// <summary>
    /// ファイル一覧で行をまとめて選んだことを画面に伝える。
    ///
    /// **何行を巻き込んだのかは一覧を見ても数えられない。** 一括入力は選択行すべてを
    /// 書き換え、値が空ならタグを消す。押す前に対象の規模が分かるようにしておく。
    ///
    /// 選択そのものは View（<c>DataGrid</c>）が持つ。ここは件数を受け取るだけ。
    /// </summary>
    /// <param name="count">選ばれている行数。</param>
    public void NotifyTracksSelected(int count)
    {
        StatusText = count == 0
            ? "選べる行がありません。絞り込みを見直してください。"
            : string.Create(CultureInfo.CurrentCulture, $"ファイル一覧の {count:N0} 行を選びました。");
    }

    /// <summary>
    /// 絞り込みが変わったら一覧を絞り直す。
    /// </summary>
    partial void OnTrackFilterTextChanged(string value)
    {
        _trackRefresher?.Request();
    }

    /// <summary>
    /// 絞り込みが変わったら一覧を絞り直す。
    /// </summary>
    partial void OnShowOnlyEmptyFieldsChanged(bool value)
    {
        _trackRefresher?.Request();
    }

    /// <summary>
    /// 絞り込みが変わったら一覧を絞り直す。
    /// </summary>
    partial void OnShowOnlyEditedTracksChanged(bool value)
    {
        _trackRefresher?.Request();
    }

    /// <summary>
    /// 相対パスに対応する行を、ファイル一覧で選べる状態にする。
    ///
    /// 検査結果の差分明細から手編集へ移るための導線（docs/SPEC.md 5.3）。
    /// ツリーの選択も絞り込みも、**対象行が隠れる場合だけ**動かす。利用者が組み立てた
    /// 絞り込みを毎回捨てると、明細を 1 行ずつ確認していく作業が成り立たなくなる。
    ///
    /// タブの切り替えと一覧のスクロールは <see cref="TrackRevealRequested"/> を受けた View が行う。
    /// </summary>
    /// <param name="relativePath">見せたいファイルの相対パス。</param>
    public void RevealTrack(string relativePath)
    {
        TrackRowViewModel? row = FindTrackRow(relativePath);

        if (row is null)
        {
            StatusText = $"「{relativePath}」はファイル一覧にありません。再スキャンで消えた可能性があります。";
            return;
        }

        SelectFolderFor(row);

        List<string> released = ReleaseFiltersHiding(row);

        StatusText = released.Count == 0
            ? $"ファイル一覧で「{row.FileName}」を選びました。"
            : $"ファイル一覧で「{row.FileName}」を選びました。 絞り込みを解除しました（{string.Join(" / ", released)}）。";

        TrackRevealRequested?.Invoke(this, row);
    }

    /// <summary>
    /// ファイル一覧の行をエクスプローラーで表示する。
    ///
    /// 一覧で気になるファイルを見つけたあと、パスを目で読んでエクスプローラーを
    /// 開き直す手間を省くための導線。**アプリからファイルを操作するわけではない。**
    /// </summary>
    /// <param name="row">対象の行。null なら何もしない。</param>
    [RelayCommand]
    private void RevealInExplorer(TrackRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        string fullPath = row.Tags.FullPath;

        try
        {
            if (ExplorerLauncher.RevealFile(fullPath))
            {
                StatusText = $"エクスプローラーで「{row.FileName}」を表示しました。";
                return;
            }

            StatusText = Directory.Exists(Path.GetDirectoryName(fullPath))
                ? $"「{row.FileName}」が見つからないため、フォルダだけを開きました。"
                : $"「{row.RelativePath}」が見つかりません。再スキャンしてください。";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            Log.Error(ex, "エクスプローラーを開けなかった path={Path}", fullPath);
            StatusText = $"エクスプローラーを開けませんでした: {ex.Message}";
        }
    }

    /// <summary>
    /// 対象行を含むフォルダをツリーで選び直す。見つからなければライブラリ全体に戻す。
    /// </summary>
    /// <param name="row">対象の行。</param>
    private void SelectFolderFor(TrackRowViewModel row)
    {
        FolderNodeViewModel? root = FolderTree.FirstOrDefault();

        if (root is null)
        {
            return;
        }

        FolderNodeViewModel target = root.Locate(row.FolderPath) ?? root;

        // 同じフォルダを選び直しても ApplyFolderFilter は走らない（値が変わらないため）。
        // その場合は既に対象行が Tracks に入っているので、選択状態だけ合わせれば足りる。
        SelectedFolder = target;
        target.IsSelected = true;
    }

    /// <summary>
    /// 対象行を隠している絞り込みだけを解除する。
    /// </summary>
    /// <param name="row">対象の行。</param>
    /// <returns>解除した絞り込みの名前。何も解除しなかった場合は空。</returns>
    private List<string> ReleaseFiltersHiding(TrackRowViewModel row)
    {
        List<string> released = [];

        if (MatchesTrackFilter(row))
        {
            return released;
        }

        if (ShowOnlyEditedTracks && !row.IsEdited)
        {
            ShowOnlyEditedTracks = false;
            released.Add("編集した行のみ");
        }

        if (ShowOnlyEmptyFields && !row.HasEmptyField)
        {
            ShowOnlyEmptyFields = false;
            released.Add("空欄のある行のみ");
        }

        if (!MatchesTrackFilter(row))
        {
            TrackFilterText = string.Empty;
            released.Add("検索文字列");
        }

        return released;
    }

    /// <summary>
    /// 一覧に出すかどうかを判定する（docs/SPEC.md 5.2 の絞り込み）。
    /// </summary>
    private bool MatchesTrackFilter(object item)
    {
        if (item is not TrackRowViewModel row)
        {
            return false;
        }

        if (ShowOnlyEditedTracks && !row.IsEdited)
        {
            return false;
        }

        if (ShowOnlyEmptyFields && !row.HasEmptyField)
        {
            return false;
        }

        return TrackFilterText.Length == 0
            || row.SearchText.Contains(TrackFilterText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 選択したルールの差分明細を下段に出す。
    /// </summary>
    partial void OnSelectedRuleChanged(RuleResultViewModel? value)
    {
        RefreshInspectionChanges();
    }

    /// <summary>
    /// 選択中ルール（<see cref="SelectedRule"/>）の明細で下段グリッドを作り直す。
    ///
    /// <c>OnSelectedRuleChanged</c> と <see cref="RemoveSucceededFromInspection"/> の
    /// 両方から呼ぶ。後者はルール行を差し替えたあと、選択中ルールが差し替わった場合に
    /// 下段の表示を最新化する必要がある。
    /// </summary>
    private void RefreshInspectionChanges()
    {
        InspectionChanges.Clear();
        SelectedChange = null;

        if (SelectedRule is null)
        {
            return;
        }

        foreach (TagChangeViewModel change in SelectedRule.ScopedChanges)
        {
            InspectionChanges.Add(change);
        }
    }

    /// <summary>
    /// 差分明細の絞り込みビューを用意する。初回だけ作ればよい。
    ///
    /// **絞り込みが要求されるまで作らない。** <c>CollectionView</c> はスレッド親和性を持ち、
    /// 作った時点から <see cref="InspectionChanges"/> の詰め替えが 1 本のスレッドに縛られる。
    /// 使いもしない絞り込みのために、明細を作り直すすべての経路へ制約を広げない。
    /// </summary>
    private void SetUpInspectionChangeView()
    {
        if (_inspectionChangeView is not null)
        {
            return;
        }

        _inspectionChangeView = CollectionViewSource.GetDefaultView(InspectionChanges);
        _inspectionChangeView.Filter = MatchesInspectionChangeFilter;
        _inspectionChangeRefresher = new GridViewRefresher(_inspectionChangeView);
    }

    /// <summary>
    /// 差分明細を一覧に出すかどうかを判定する。
    /// </summary>
    /// <param name="item">判定する行。</param>
    /// <returns>出すなら true。</returns>
    private bool MatchesInspectionChangeFilter(object item)
    {
        if (item is not TagChangeViewModel change)
        {
            return false;
        }

        return !ShowOnlySelectedChanges || change.IsSelected;
    }

    /// <summary>
    /// 絞り込みが変わったら差分明細を絞り直す。
    ///
    /// ビューはここで初めて用意する。<see cref="ICollectionView.Filter"/> の設定自体が
    /// 掛け直しを伴うので、作った直後は <c>Request</c> が二度目になるだけで害は無い。
    /// </summary>
    partial void OnShowOnlySelectedChangesChanged(bool value)
    {
        SetUpInspectionChangeView();
        _inspectionChangeRefresher?.Request();
    }

    /// <summary>
    /// 検査結果の絞り込み対象になっているフォルダ名。絞り込んでいなければ空。
    /// </summary>
    private string InspectionScopeLabel =>
        LimitInspectionToSelectedFolder && SelectedFolder is { RelativePath.Length: > 0 } folder
            ? folder.Name
            : string.Empty;

    /// <summary>
    /// 検査結果の絞り込み判定を作る。絞り込まないときは null を返す。
    ///
    /// ルート（相対パスが空）を選んでいるときは全件が対象。
    /// <see cref="ApplyFolderFilter"/> と基準を揃える。
    /// </summary>
    private Func<TagChangeViewModel, bool>? BuildInspectionScope()
    {
        if (!LimitInspectionToSelectedFolder || SelectedFolder is not { RelativePath.Length: > 0 } folder)
        {
            return null;
        }

        string ancestor = folder.RelativePath;

        return change => IsUnder(change.FolderPath, ancestor);
    }

    /// <summary>
    /// 検査結果を今の絞り込み範囲で作り直す。
    ///
    /// **チェック状態には触れない。** 範囲外の <see cref="TagChange.IsSelected"/> はそのまま残し、
    /// 絞り込みを外せば元の選択が戻るようにする。
    ///
    /// **<c>DataGrid</c> がセルの編集トランザクションを開いている最中に呼ばないこと。**
    /// 表示用コレクションを詰め替えるため、<see cref="OnInspectionSelectionChanged"/> からは呼べない。
    /// </summary>
    /// <param name="preferredSelection">選択し直したいルール行。無ければ null。</param>
    private void ApplyInspectionScope(RuleResultViewModel? preferredSelection = null)
    {
        Func<TagChangeViewModel, bool>? scope = BuildInspectionScope();

        foreach (RuleResultViewModel rule in _allRuleResults)
        {
            rule.SetScope(scope);
        }

        RuleResultViewModel? previous = preferredSelection ?? SelectedRule;

        RuleResults.Clear();

        // 配下に検出が無いルールは行ごと落とす。0 件の行を残しても情報量が無く、
        // 検査直後に 0 件のルールを出さないのと基準が揃う（RunInspection）。
        foreach (RuleResultViewModel rule in _allRuleResults.Where(rule => rule.Count > 0))
        {
            RuleResults.Add(rule);
        }

        RuleResultViewModel? next = previous is not null && RuleResults.Contains(previous)
            ? previous
            : RuleResults.FirstOrDefault();

        if (ReferenceEquals(SelectedRule, next))
        {
            // OnSelectedRuleChanged が発火しない。明細は範囲が変わっているので自分で張り直す。
            TagChangeViewModel? selectedChange = SelectedChange;

            RefreshInspectionChanges();

            // **範囲に残っている行の選択は捨てない。** 適用のたびに下段の選択が飛ぶと、
            // 明細を 1 行ずつ潰していく作業のほうが壊れる。
            if (selectedChange is not null && InspectionChanges.Contains(selectedChange))
            {
                SelectedChange = selectedChange;
            }
        }
        else
        {
            SelectedRule = next;
        }

        UpdateInspectionSelection(isDefault: false);

        // 下段の一括操作の活性は SelectedRule.FixableCount で決まる。選択中ルールが同じままでも
        // 範囲が変われば件数が変わるので、[NotifyCanExecuteChangedFor] 任せにはできない。
        // 上段は RuleResults の CollectionChanged で再評価される（コンストラクタ）。
        SelectAllChangesCommand.NotifyCanExecuteChanged();
        DeselectAllChangesCommand.NotifyCanExecuteChanged();
        InvertChangesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 絞り込み範囲に入っている修正案。適用と CSV 出力はここを見る。
    /// </summary>
    private IEnumerable<TagChange> ScopedChanges()
    {
        return _allRuleResults
            .SelectMany(rule => rule.ScopedChanges)
            .Select(change => change.Change);
    }

    /// <summary>
    /// 適用に成功した項目だけを検査結果から取り除く。
    ///
    /// 全クリアすると「まだ直っていない」ように見せてしまう項目まで消えてしまうが、
    /// 未対象・失敗・不一致・競合の項目はチェック状態を保ったまま残したい
    /// （検査結果を保持してほしいという要望への対応）。触れていないルール行は
    /// 同一インスタンスを使い回し、ヘッダーのチェック状態を意味なくリセットしない。
    ///
    /// <c>internal</c> にしてあるのはテストのため。<see cref="ApplyAsync"/> は
    /// 確認ダイアログ（<c>MessageBox.Show</c>）を経由するため自動テストで直接実行できず、
    /// このメソッド単体をテストから呼んで検証する。
    /// </summary>
    internal void RemoveSucceededFromInspection(IReadOnlySet<TagChangeKey> succeededFields)
    {
        if (_lastInspection is null || succeededFields.Count == 0)
        {
            return;
        }

        _lastInspection = _lastInspection.RemoveChanges(succeededFields);

        RuleResultViewModel? selectedRule = SelectedRule;
        RuleResultViewModel? replacementForSelectedRule = null;
        bool selectedRuleRemoved = selectedRule is not null;
        List<RuleResultViewModel> updated = [];

        foreach (RuleResultViewModel existing in _allRuleResults)
        {
            bool touched = existing.Result.Changes.Any(
                change => succeededFields.Contains(TagChangeKey.From(change)));

            if (!touched)
            {
                updated.Add(existing);

                if (ReferenceEquals(existing, selectedRule))
                {
                    selectedRuleRemoved = false;
                }

                continue;
            }

            existing.ChangeSelectionChanged -= OnInspectionSelectionChanged;

            TagChange[] remaining =
            [
                .. existing.Result.Changes.Where(change => !succeededFields.Contains(TagChangeKey.From(change))),
            ];

            if (remaining.Length == 0)
            {
                // ルール行ごと落とす。空のルールをヘッダーだけ残しても情報量が無い
                // （RunInspection が検査直後に 0 件のルールを画面に出さないのと基準を揃える）。
                continue;
            }

            RuleResultViewModel replacement = new(existing.Result with { Changes = remaining });
            replacement.ChangeSelectionChanged += OnInspectionSelectionChanged;
            updated.Add(replacement);

            if (ReferenceEquals(existing, selectedRule))
            {
                replacementForSelectedRule = replacement;
                selectedRuleRemoved = false;
            }
        }

        _allRuleResults.Clear();
        _allRuleResults.AddRange(updated);

        if (selectedRuleRemoved)
        {
            // 選択中ルールが丸ごと消えた。RunInspection の既定選択と同じく先頭へ切り替える。
            SelectedRule = null;
        }

        // 表示用のルール行の詰め替えと選択の解決は絞り込み側にまとめてある。
        // **差し替えで新しく作った RuleResultViewModel にも範囲を当て直す必要がある。**
        ApplyInspectionScope(replacementForSelectedRule);
    }

    /// <summary>
    /// チェックされた修正案を書き込む。docs/SPEC.md 9章の工程 4〜7。
    ///
    /// 適用直前のスナップショットは <see cref="ApplyService"/> が必ず取る。
    /// 書き込み後の読み戻し照合も同サービスが行い、不一致は握りつぶさず報告する。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (_lastScan is null || _lastInspection is null)
        {
            return;
        }

        // **画面に出ていない項目は書き込まない。** 絞り込み中に全体へ適用すると、
        // 1 フォルダだけ直しているつもりの操作がライブラリ全体への書き込みになる。
        TagChange[] targets = [.. ScopedChanges().Where(change => change.IsSelected && change.HasFix)];

        if (targets.Length == 0)
        {
            StatusText = "適用対象がありません。";
            return;
        }

        if (!ConfirmApply(targets, InspectionScopeLabel))
        {
            return;
        }

        IsScanning = true;
        ApplyIssues.Clear();
        ProgressValue = 0;
        ProgressMaximum = 1;
        StatusText = "適用しています…";

        ApplyResult? result = null;

        try
        {
            Progress<ApplyProgress> progress = new(report =>
            {
                ProgressMaximum = Math.Max(report.Total, 1);
                ProgressValue = report.Completed;
            });

            result = await _applyService
                .ApplyAsync(
                    _lastScan,
                    targets,
                    portableLibraryPath: TagWriter.GetPortableLibraryPath(),
                    progress: progress)
                .ConfigureAwait(true);

            ShowApplyResult(result);
        }
        catch (Exception ex)
        {
            StatusText = $"適用に失敗しました: {ex.Message}";
            Log.Error(ex, "適用に失敗した root={Root}", LibraryRoot);
        }
        finally
        {
            IsScanning = false;
        }

        // 成功した項目だけ検査結果から取り除く。失敗・不一致・競合の項目とチェック状態は残す
        // （例外で result が取れなかった場合は刈り込みをスキップする）。
        if (result is not null)
        {
            RemoveSucceededFromInspection(result.GetSucceededFields(targets));
        }

        // 適用でタグの値が変わっているのでファイル一覧は読み直す。検査結果は上で刈り込み済みなので
        // 巻き込まない（RescanLibraryAsync は ScanAsync と違い検査結果・ApplyIssues に触れない）。
        await RescanLibraryAsync().ConfigureAwait(true);
        RefreshBackups();
    }

    /// <summary>
    /// 適用してよいかを確認する。書き込みは取り消しにくいので、件数と内訳を示してから実行する。
    /// </summary>
    /// <param name="targets">書き込む修正案。</param>
    /// <param name="scopeLabel">
    /// 絞り込み中の対象フォルダ名。絞り込んでいなければ空。
    /// **絞っているときは必ず示す。** 全体に効いたと思わせたまま一部だけ書き込むのは嘘になる。
    /// </param>
    private static bool ConfirmApply(IReadOnlyList<TagChange> targets, string scopeLabel)
    {
        string breakdown = string.Join(
            Environment.NewLine,
            targets.GroupBy(change => change.RuleId)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"  {group.Key}  {group.Count():N0} 項目"));

        int fileCount = targets
            .Select(change => change.RelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        string scopeLine = scopeLabel.Length == 0
            ? string.Empty
            : $"対象フォルダ: {scopeLabel}（配下のみ）" + Environment.NewLine + Environment.NewLine;

        string message = scopeLine
            + string.Create(
                CultureInfo.CurrentCulture,
                $"{fileCount:N0} ファイルに {targets.Count:N0} 項目を書き込みます。")
            + Environment.NewLine + Environment.NewLine
            + breakdown
            + Environment.NewLine + Environment.NewLine
            + "書き込みの直前にタグのスナップショットを自動で取ります。"
            + Environment.NewLine
            + "適用後は全項目を読み戻して照合します。";

        return MessageBox.Show(
            message,
            "タグを適用しますか？",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;
    }

    /// <summary>
    /// 適用結果を画面とログに出す。**不一致は必ず知らせる。**
    /// </summary>
    private void ShowApplyResult(ApplyResult result)
    {
        foreach (ApplyFailure failure in result.Failures)
        {
            ApplyIssues.Add($"[書き込み失敗] {failure.RelativePath} — {failure.Message}");
        }

        foreach (VerificationMismatch mismatch in result.Mismatches)
        {
            ApplyIssues.Add($"[読み戻し不一致] {mismatch.Summary}");
        }

        foreach (ApplyConflict conflict in result.Conflicts)
        {
            ApplyIssues.Add($"[修正案の競合] {conflict.Summary}");
        }

        string summary = string.Create(
            CultureInfo.CurrentCulture,
            $"適用しました。{result.SucceededFiles:N0} / {result.AttemptedFiles:N0} ファイル、{result.AppliedChanges:N0} 項目。")
            + $" バックアップ: {Path.GetFileName(result.BackupDirectory)}";

        if (!result.IsClean)
        {
            summary += $" ⚠ 要確認 {ApplyIssues.Count} 件";
        }

        StatusText = summary;
        InspectionSummary = summary;

        Log.Information(
            "適用完了 対象={Attempted} 成功={Succeeded} 項目={Applied} 失敗={Failures} 不一致={Mismatches} 競合={Conflicts} backup={Backup}",
            result.AttemptedFiles,
            result.SucceededFiles,
            result.AppliedChanges,
            result.Failures.Count,
            result.Mismatches.Count,
            result.Conflicts.Count,
            result.BackupDirectory);

        foreach (string issue in ApplyIssues)
        {
            Log.Warning("適用の要確認項目 {Issue}", issue);
        }

        if (result.IsClean)
        {
            return;
        }

        // 書き込めたことと意図した値が入っていることは別。黙って終わらせない。
        MessageBox.Show(
            $"{ApplyIssues.Count} 件の要確認項目があります。検査結果タブの下部に一覧を表示しました。"
            + Environment.NewLine + Environment.NewLine
            + $"適用前の状態は「{Path.GetFileName(result.BackupDirectory)}」から復元できます。",
            "適用は完了しましたが確認が必要です",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>適用できるか。</summary>
    private bool CanApply()
    {
        return !IsScanning && CanApplyChanges;
    }

    /// <summary>検査を実行できるか。</summary>
    private bool CanInspect()
    {
        return !IsScanning && _lastScan is not null;
    }

    /// <summary>バックアップを取得できるか。</summary>
    private bool CanCreateBackup()
    {
        return !IsScanning && _lastScan is not null;
    }

    /// <summary>差分を確認できるか。</summary>
    private bool CanPreviewRestore()
    {
        return !IsScanning && SelectedBackup is not null;
    }

    /// <summary>復元を実行できるか。</summary>
    private bool CanRestore()
    {
        return !IsScanning && IsRestoreReady;
    }

    /// <summary>スキャンを開始できるか。</summary>
    private bool CanStartScan()
    {
        return !IsScanning;
    }

    /// <summary>再スキャンできるか。</summary>
    private bool CanRescan()
    {
        return !IsScanning && !string.IsNullOrEmpty(LibraryRoot);
    }

    /// <summary>
    /// ツリーの選択が変わったら一覧を絞り込む。
    /// 検査結果は「選択フォルダのみ」が有効なときだけ追随させる。
    /// </summary>
    partial void OnSelectedFolderChanged(FolderNodeViewModel? value)
    {
        ApplyFolderFilter(value);

        if (LimitInspectionToSelectedFolder)
        {
            ApplyInspectionScope();
        }
    }

    /// <summary>
    /// 検査結果の絞り込みの入切が変わったので、範囲を作り直す。
    /// </summary>
    partial void OnLimitInspectionToSelectedFolderChanged(bool value)
    {
        ApplyInspectionScope();
    }

    /// <summary>
    /// ライブラリを走査してタグを読み取り、一覧とツリーを組み立てる。
    /// </summary>
    private async Task ScanAsync()
    {
        if (string.IsNullOrEmpty(LibraryRoot))
        {
            return;
        }

        // 編集は読み取り時点のタグを土台にしている。読み直したら足場が変わるので捨てる。
        // 捨ててよいかは呼び出し側が確認済み（ConfirmDiscardManualEdits）。
        _manualEdits.Clear();

        ClearRuleResults();
        InspectionChanges.Clear();
        UnknownValues.Clear();
        ApplyIssues.Clear();
        InspectionSummary = "「検査」を押すと原則違反を洗い出します。";
        UnknownValueSummary = "検査すると、辞書に無い値がここに集まります。";
        CanApplyChanges = false;
        HasInspectionResult = false;
        SelectedChange = null;
        _lastInspection = null;
        _lastContext = null;

        await RescanLibraryAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// ライブラリを走査してタグを読み取り、一覧とツリーだけを組み立て直す。
    ///
    /// **検査結果（<see cref="RuleResults"/> 等）と保留中の手編集には触れない。**
    /// 適用直後のように「タグの値だけ最新化したいが検査結果は残したい」場面から呼ばれる
    /// （<see cref="ApplyAsync"/>）。全クリアしたい場合は <see cref="ScanAsync"/> を使うこと。
    /// </summary>
    private async Task RescanLibraryAsync()
    {
        if (string.IsNullOrEmpty(LibraryRoot))
        {
            return;
        }

        IsScanning = true;

        Failures.Clear();
        Tracks.Clear();

        // 読み直しに失敗・中止したら一覧は空のまま戻る。書き出せる行が無いので出口も閉じる。
        HasTracks = false;
        FolderTree.Clear();
        ProgressValue = 0;
        ProgressMaximum = 1;
        StatusText = "スキャンしています…";

        _scanCancellation = new CancellationTokenSource();

        Progress<ScanProgress> progress = new(report =>
        {
            ProgressMaximum = Math.Max(report.Total, 1);
            ProgressValue = report.Completed;
            StatusText = string.Create(
                CultureInfo.CurrentCulture,
                $"読み取り中 {report.Completed} / {report.Total} — {report.CurrentRelativePath}");
        });

        try
        {
            ScanResult result = await _scanner
                .ScanAsync(LibraryRoot, progress, _scanCancellation.Token)
                .ConfigureAwait(true);

            Load(result);

            Log.Information(
                "スキャン完了 root={Root} 件数={Count} 失敗={Failures} 所要={Elapsed}",
                result.LibraryRoot,
                result.Tracks.Count,
                result.Failures.Count,
                result.Elapsed);
        }
        catch (OperationCanceledException)
        {
            StatusText = "スキャンを中止しました。";
            Log.Information("スキャンを中止した root={Root}", LibraryRoot);
        }
        catch (Exception ex)
        {
            StatusText = $"スキャンに失敗しました: {ex.Message}";
            Log.Error(ex, "スキャンに失敗した root={Root}", LibraryRoot);
        }
        finally
        {
            _scanCancellation.Dispose();
            _scanCancellation = null;
            IsScanning = false;
        }
    }

    /// <summary>
    /// スキャン結果を画面に反映する。
    /// </summary>
    private void Load(ScanResult result)
    {
        _lastScan = result;
        _allTracks = [.. result.Tracks.Select(track => new TrackRowViewModel(track, _manualEdits))];
        HasTracks = _allTracks.Count > 0;

        foreach (ScanFailure failure in result.Failures)
        {
            Failures.Add(failure);
        }

        FolderNodeViewModel root = FolderNodeViewModel.BuildTree(
            Path.GetFileName(result.LibraryRoot.TrimEnd(Path.DirectorySeparatorChar)),
            result.Tracks.Select(track => track.RelativePath));

        FolderTree.Add(root);
        SelectedFolder = root;
        RefreshBackups();

        int splitCount = _allTracks.Count(track => track.HasSplitValues);

        StatusText = string.Create(
            CultureInfo.CurrentCulture,
            $"{result.Tracks.Count:N0} 件を読み取りました（{result.Elapsed.TotalSeconds:F1} 秒）。"
            + $" 読み取り失敗 {result.Failures.Count} 件 / 複数値として格納されているファイル {splitCount} 件。");
    }

    /// <summary>
    /// 選択されたフォルダ配下のファイルだけを一覧に出す。
    /// </summary>
    private void ApplyFolderFilter(FolderNodeViewModel? folder)
    {
        Tracks.Clear();

        IEnumerable<TrackRowViewModel> filtered = folder is null || folder.RelativePath.Length == 0
            ? _allTracks
            : _allTracks.Where(track => IsUnder(track.FolderPath, folder.RelativePath));

        foreach (TrackRowViewModel track in filtered)
        {
            Tracks.Add(track);
        }

        SetUpTrackView();
    }

    /// <summary>
    /// ファイル一覧の絞り込みビューを用意する。初回だけ作ればよい。
    /// </summary>
    private void SetUpTrackView()
    {
        if (_trackView is not null)
        {
            return;
        }

        _trackView = CollectionViewSource.GetDefaultView(Tracks);
        _trackView.Filter = MatchesTrackFilter;
        _trackRefresher = new GridViewRefresher(_trackView);
    }

    /// <summary>
    /// フォルダパスが指定フォルダの配下かどうかを判定する。
    /// </summary>
    private static bool IsUnder(string folderPath, string ancestorPath)
    {
        if (folderPath.Equals(ancestorPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return folderPath.StartsWith(ancestorPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
