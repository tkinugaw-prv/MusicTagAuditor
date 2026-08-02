using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MusicTagger.Core.Backup;
using MusicTagger.Core.Dictionary;
using MusicTagger.Core.Inspection;
using MusicTagger.Core.Models;
using MusicTagger.Core.Scanning;
using MusicTagger.TagIo;
using Serilog;

namespace MusicTagger.App.ViewModels;

/// <summary>
/// メインウィンドウのビューモデル。
/// 段階 2 までの範囲。スキャンと読み取り、一覧表示、バックアップと復元を扱う。
/// 検査ルールによる一括適用は段階 3 以降（docs/SPEC.md 12章）。
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

    /// <summary>正規化辞書の索引。</summary>
    private readonly DictionaryIndex _dictionary;

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

    /// <summary>
    /// ビューモデルを初期化する。
    /// </summary>
    /// <param name="scanner">ライブラリスキャナ。</param>
    /// <param name="snapshotService">スナップショットの取得・読み込み。</param>
    /// <param name="restoreService">復元処理。</param>
    /// <param name="inspectionEngine">検査エンジン。</param>
    /// <param name="dictionary">正規化辞書の索引。</param>
    public MainViewModel(
        LibraryScanner scanner,
        SnapshotService snapshotService,
        RestoreService restoreService,
        InspectionEngine inspectionEngine,
        DictionaryIndex dictionary)
    {
        _scanner = scanner;
        _snapshotService = snapshotService;
        _restoreService = restoreService;
        _inspectionEngine = inspectionEngine;
        _dictionary = dictionary;
    }

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
        if (_lastScan is null)
        {
            return;
        }

        RuleResults.Clear();
        InspectionChanges.Clear();

        try
        {
            InspectionContext context = new(_lastScan, _dictionary);
            InspectionResult result = _inspectionEngine.Inspect(context);

            foreach (RuleResult rule in result.Results.Where(rule => rule.Changes.Count > 0))
            {
                RuleResults.Add(new RuleResultViewModel(rule));
            }

            SelectedRule = RuleResults.FirstOrDefault();

            int selected = result.AllChanges.Count(change => change.IsSelected);
            int holds = result.AllChanges.Count(change => change.HoldReason != HoldReason.None);

            InspectionSummary = string.Create(
                CultureInfo.CurrentCulture,
                $"検出 {result.TotalChanges:N0} 件 / 既定で選択 {selected:N0} 件 / 保留 {holds:N0} 件"
                + $"（{result.Elapsed.TotalSeconds:F2} 秒）");

            StatusText = InspectionSummary;

            Log.Information(
                "検査完了 検出={Total} 選択={Selected} 保留={Holds} 所要={Elapsed}",
                result.TotalChanges,
                selected,
                holds,
                result.Elapsed);
        }
        catch (Exception ex)
        {
            InspectionSummary = $"検査に失敗しました: {ex.Message}";
            Log.Error(ex, "検査に失敗した root={Root}", LibraryRoot);
        }
    }

    /// <summary>
    /// 選択したルールの差分明細を下段に出す。
    /// </summary>
    partial void OnSelectedRuleChanged(RuleResultViewModel? value)
    {
        InspectionChanges.Clear();

        if (value is null)
        {
            return;
        }

        foreach (TagChange change in value.Result.Changes)
        {
            InspectionChanges.Add(change);
        }
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
        InspectionSummary = "「検査」を押すと原則違反を洗い出します。";
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
