using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MusicTagger.Core.Applying;
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
/// 段階 4 までの範囲。スキャンと読み取り、一覧表示、バックアップと復元、検査と適用を扱う。
/// 辞書編集と手編集は段階 5 以降（docs/SPEC.md 12章）。
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

    /// <summary>
    /// ビューモデルを初期化する。
    /// </summary>
    /// <param name="scanner">ライブラリスキャナ。</param>
    /// <param name="snapshotService">スナップショットの取得・読み込み。</param>
    /// <param name="restoreService">復元処理。</param>
    /// <param name="inspectionEngine">検査エンジン。</param>
    /// <param name="dictionary">正規化辞書の索引。</param>
    /// <param name="applyService">適用処理。</param>
    public MainViewModel(
        LibraryScanner scanner,
        SnapshotService snapshotService,
        RestoreService restoreService,
        InspectionEngine inspectionEngine,
        DictionaryIndex dictionary,
        ApplyService applyService)
    {
        _scanner = scanner;
        _snapshotService = snapshotService;
        _restoreService = restoreService;
        _inspectionEngine = inspectionEngine;
        _dictionary = dictionary;
        _applyService = applyService;
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
        if (_lastScan is null)
        {
            return;
        }

        RuleResults.Clear();
        InspectionChanges.Clear();
        ApplyIssues.Clear();
        CanApplyChanges = false;
        _lastInspection = null;

        try
        {
            InspectionContext context = new(_lastScan, _dictionary);
            InspectionResult result = _inspectionEngine.Inspect(context);

            foreach (RuleResult rule in result.Results.Where(rule => rule.Changes.Count > 0))
            {
                RuleResults.Add(new RuleResultViewModel(rule));
            }

            _lastInspection = result;
            SelectedRule = RuleResults.FirstOrDefault();

            int selected = result.AllChanges.Count(change => change.IsSelected);
            int holds = result.AllChanges.Count(change => change.HoldReason != HoldReason.None);

            InspectionSummary = string.Create(
                CultureInfo.CurrentCulture,
                $"検出 {result.TotalChanges:N0} 件 / 既定で選択 {selected:N0} 件 / 保留 {holds:N0} 件"
                + $"（{result.Elapsed.TotalSeconds:F2} 秒）");

            StatusText = InspectionSummary;
            CanApplyChanges = selected > 0;

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
        ApplyIssues.Clear();
        InspectionSummary = "「検査」を押すと原則違反を洗い出します。";
        CanApplyChanges = false;
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
