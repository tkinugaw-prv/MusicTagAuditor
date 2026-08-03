using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MusicTagger.Core.Applying;
using MusicTagger.Core.Backup;
using MusicTagger.Core.Dictionary;
using MusicTagger.Core.Export;
using MusicTagger.Core.Inspection;
using MusicTagger.Core.Models;
using MusicTagger.Core.Scanning;
using MusicTagger.TagIo;
using Serilog;

namespace MusicTagger.App.ViewModels;

/// <summary>
/// メインウィンドウのビューモデル。
/// 段階 5 までの範囲。スキャンと読み取り、一覧表示、バックアップと復元、検査と適用、
/// 辞書の閲覧・編集を扱う。手編集は段階 6 以降（docs/SPEC.md 12章）。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
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

    /// <summary>直近の検査結果。適用対象はここから取る。</summary>
    private InspectionResult? _lastInspection;

    /// <summary>直近のスキャン結果。バックアップの取得元になる。</summary>
    private ScanResult? _lastScan;

    /// <summary>直近のスキャン結果。ツリーでの絞り込みはこの一覧から行う。</summary>
    private IReadOnlyList<TrackRowViewModel> _allTracks = [];

    /// <summary>実行中のスキャンをキャンセルするためのトークンソース。</summary>
    private CancellationTokenSource? _scanCancellation;

    /// <summary>開いているライブラリのルート。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RescanCommand))]
    private string? _libraryRoot;

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

    /// <summary>検査結果の差分明細で選択中の行。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSelectedChangeToDictionaryCommand))]
    private TagChange? _selectedChange;

    /// <summary>未知の値の一覧で選択中の行。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddUnknownValueToDictionaryCommand))]
    private UnknownValue? _selectedUnknownValue;

    /// <summary>未知の値の要約。</summary>
    [ObservableProperty]
    private string _unknownValueSummary = "検査すると、辞書に無い値がここに集まります。";

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
    public MainViewModel(
        LibraryScanner scanner,
        SnapshotService snapshotService,
        RestoreService restoreService,
        InspectionEngine inspectionEngine,
        DictionaryStore dictionaryStore,
        DictionaryViewModel dictionaryViewModel,
        ApplyService applyService)
    {
        ArgumentNullException.ThrowIfNull(dictionaryViewModel);

        _scanner = scanner;
        _snapshotService = snapshotService;
        _restoreService = restoreService;
        _inspectionEngine = inspectionEngine;
        _dictionaryStore = dictionaryStore;
        _applyService = applyService;

        Dictionary = dictionaryViewModel;

        // 辞書を保存したら検査をやり直す。タグは変わっていないので再スキャンは要らない。
        Dictionary.Saved += OnDictionarySaved;
    }

    /// <summary>辞書タブ。</summary>
    public DictionaryViewModel Dictionary { get; }

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
    public ObservableCollection<TagChange> InspectionChanges { get; } = [];

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
    /// 起動時に指定されたライブラリをそのまま開く。
    /// 動作確認や再現手順の共有で、毎回フォルダを選び直さずに済ませるために使う。
    /// </summary>
    /// <param name="libraryRoot">ライブラリのルートパス。</param>
    public async Task OpenAsync(string libraryRoot)
    {
        LibraryRoot = libraryRoot;
        await ScanAsync();
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

        LibraryRoot = dialog.FolderName;
        await ScanAsync();
    }

    /// <summary>
    /// 開いているライブラリを再スキャンする。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRescan))]
    private async Task RescanAsync()
    {
        await ScanAsync();
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

        RuleResults.Clear();
        InspectionChanges.Clear();
        UnknownValues.Clear();
        ApplyIssues.Clear();
        CanApplyChanges = false;
        HasInspectionResult = false;
        _lastInspection = null;

        try
        {
            InspectionContext context = new(_lastScan, _dictionaryStore.Index);
            InspectionResult result = _inspectionEngine.Inspect(context);

            foreach (RuleResult rule in result.Results.Where(rule => rule.Changes.Count > 0))
            {
                RuleResults.Add(new RuleResultViewModel(rule));
            }

            _lastInspection = result;
            SelectedRule = RuleResults.FirstOrDefault();

            LoadUnknownValues(result);

            int selected = result.AllChanges.Count(change => change.IsSelected);
            int holds = result.AllChanges.Count(change => change.HoldReason != HoldReason.None);

            InspectionSummary = string.Create(
                CultureInfo.CurrentCulture,
                $"検出 {result.TotalChanges:N0} 件 / 既定で選択 {selected:N0} 件 / 保留 {holds:N0} 件"
                + $"（{result.Elapsed.TotalSeconds:F2} 秒）");

            StatusText = InspectionSummary;
            CanApplyChanges = selected > 0;
            HasInspectionResult = true;

            Log.Information(
                "検査完了 検出={Total} 選択={Selected} 保留={Holds} 未知の値={Unknown} 所要={Elapsed}",
                result.TotalChanges,
                selected,
                holds,
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

        UnknownValue unknown = UnknownValueCollector.Collect([SelectedChange]).FirstOrDefault()
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

        try
        {
            TagDictionary edited = viewModel.Apply();

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

            Log.Information(
                "辞書に追加した value={Value} category={Category} 件数={Count}",
                unknown.Value,
                unknown.Category,
                unknown.Count);

            RunInspection();

            StatusText = $"「{unknown.Value}」を辞書に登録して再検査しました。 {InspectionSummary}";
        }
        catch (Exception ex)
        {
            StatusText = $"辞書への追加に失敗しました: {ex.Message}";
            Log.Error(ex, "辞書への追加に失敗した value={Value}", unknown.Value);
        }
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
            FileName = $"musicTagger-changes-{DateTime.Now:yyyyMMddHHmmss}.csv",
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

            ChangeCsvExporter.WriteFile(dialog.FileName, _lastInspection.AllChanges);
            File.WriteAllText(
                summaryPath,
                ChangeCsvExporter.BuildSummary(_lastInspection.AllChanges),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            StatusText = string.Create(
                CultureInfo.CurrentCulture,
                $"CSV を書き出しました（{_lastInspection.TotalChanges:N0} 件）: ")
                + $"{Path.GetFileName(dialog.FileName)} / {Path.GetFileName(summaryPath)}";

            Log.Information(
                "CSV を書き出した path={Path} summary={Summary} 件数={Count}",
                dialog.FileName,
                summaryPath,
                _lastInspection.TotalChanges);
        }
        catch (Exception ex)
        {
            StatusText = $"CSV の書き出しに失敗しました: {ex.Message}";
            Log.Error(ex, "CSV の書き出しに失敗した path={Path}", dialog.FileName);
        }
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

    /// <summary>CSV を書き出せるか。</summary>
    private bool CanExportCsv()
    {
        return _lastInspection is not null;
    }

    /// <summary>
    /// 選択したルールの差分明細を下段に出す。
    /// </summary>
    partial void OnSelectedRuleChanged(RuleResultViewModel? value)
    {
        InspectionChanges.Clear();
        SelectedChange = null;

        if (value is null)
        {
            return;
        }

        foreach (TagChange change in value.Result.Changes)
        {
            InspectionChanges.Add(change);
        }
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

        TagChange[] targets = [.. _lastInspection.AllChanges.Where(change => change.IsSelected && change.HasFix)];

        if (targets.Length == 0)
        {
            StatusText = "適用対象がありません。";
            return;
        }

        if (!ConfirmApply(targets))
        {
            return;
        }

        IsScanning = true;
        ApplyIssues.Clear();
        ProgressValue = 0;
        ProgressMaximum = 1;
        StatusText = "適用しています…";

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

        // 適用後の状態で読み直す。古い検査結果を残すと、直したものがまだ出ているように見える。
        await ScanAsync().ConfigureAwait(true);
        RefreshBackups();
    }

    /// <summary>
    /// 適用してよいかを確認する。書き込みは取り消しにくいので、件数と内訳を示してから実行する。
    /// </summary>
    private static bool ConfirmApply(IReadOnlyList<TagChange> targets)
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

        string message = string.Create(
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
    /// </summary>
    partial void OnSelectedFolderChanged(FolderNodeViewModel? value)
    {
        ApplyFolderFilter(value);
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

        IsScanning = true;
        Failures.Clear();
        Tracks.Clear();
        FolderTree.Clear();
        RuleResults.Clear();
        InspectionChanges.Clear();
        UnknownValues.Clear();
        ApplyIssues.Clear();
        InspectionSummary = "「検査」を押すと原則違反を洗い出します。";
        UnknownValueSummary = "検査すると、辞書に無い値がここに集まります。";
        CanApplyChanges = false;
        HasInspectionResult = false;
        SelectedChange = null;
        _lastInspection = null;
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
        _allTracks = [.. result.Tracks.Select(track => new TrackRowViewModel(track))];

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
