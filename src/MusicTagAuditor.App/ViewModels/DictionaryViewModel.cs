using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MusicTagAuditor.Core.Dictionary;
using Serilog;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// 辞書タブのビューモデル（docs/SPEC.md 7.3）。
///
/// **保存前に必ず検証を通す。**<see cref="DictionaryIndex"/> は重複した正規化キーを黙って捨てるため、
/// 検証なしに保存すると「登録したのに効かない」状態を作ってしまう。
/// </summary>
public sealed partial class DictionaryViewModel : ObservableObject
{
    /// <summary>辞書の保持と保存。</summary>
    private readonly DictionaryStore _store;

    /// <summary>絞り込みに使うビュー。</summary>
    private readonly List<ICollectionView> _views = [];

    /// <summary>読み込み中は変更として扱わないためのフラグ。</summary>
    private bool _isLoading;

    /// <summary>未保存の変更があるか。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    private bool _isDirty;

    /// <summary>一覧の絞り込み文字列。</summary>
    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>選択中の作曲家。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveComposerCommand))]
    private ComposerRowViewModel? _selectedComposer;

    /// <summary>選択中の人物。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemovePersonCommand))]
    private PersonRowViewModel? _selectedPerson;

    /// <summary>選択中の団体。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveEnsembleCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddEraCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveEraCommand))]
    private EnsembleRowViewModel? _selectedEnsemble;

    /// <summary>選択中の時代区分。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveEraCommand))]
    private EnsembleEraRowViewModel? _selectedEra;

    /// <summary>選択中の作品。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveWorkCommand))]
    private WorkRowViewModel? _selectedWork;

    /// <summary>選択中の個別例外。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveAlbumOverrideCommand))]
    private AlbumOverrideRowViewModel? _selectedAlbumOverride;

    /// <summary>選択中の誤記。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveTypoCommand))]
    private TypoRowViewModel? _selectedTypo;

    /// <summary>選択中の保護対象。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveProtectedValueCommand))]
    private ProtectedValueRowViewModel? _selectedProtectedValue;

    /// <summary>誤記のテスト欄に入れた文字列（docs/SPEC.md 7.3）。</summary>
    [ObservableProperty]
    private string _typoTestInput = string.Empty;

    /// <summary>誤記のテスト結果。</summary>
    [ObservableProperty]
    private string _typoTestResult = "文字列を入れると、選択中のパターンで試した結果が出ます。";

    /// <summary>操作の結果を伝える文言。</summary>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// 読んでいる辞書の構成（docs/SPEC.md 7.3）。
    ///
    /// **どの辞書を読んでいるかが分からないと、検出結果が想定と違うときに
    /// 「ルールの誤り」なのか「別の辞書を読んでいる」のかを切り分けられない。**
    /// ログに出るのと同じ内容を画面にも出す（2026-08-12 に切り分けで手間取った）。
    /// </summary>
    [ObservableProperty]
    private string _summaryText = string.Empty;

    /// <summary>
    /// ビューモデルを初期化する。
    /// </summary>
    /// <param name="store">辞書の保持と保存。</param>
    public DictionaryViewModel(DictionaryStore store)
    {
        _store = store;

        Load();
        CheckForUpdates();
    }

    /// <summary>辞書を保存したときに発火する。検査をやり直すために使う。</summary>
    public event EventHandler? Saved;

    /// <summary>作曲家。</summary>
    public ObservableCollection<ComposerRowViewModel> Composers { get; } = [];

    /// <summary>指揮者・ソリスト。</summary>
    public ObservableCollection<PersonRowViewModel> Persons { get; } = [];

    /// <summary>演奏団体。</summary>
    public ObservableCollection<EnsembleRowViewModel> Ensembles { get; } = [];

    /// <summary>作品（docs/SPEC.md 7.4）。アルバム名の <c>{作品名}</c> の供給元。</summary>
    public ObservableCollection<WorkRowViewModel> Works { get; } = [];

    /// <summary>アルバム単位の個別例外（docs/SPEC.md 7.4.5）。</summary>
    public ObservableCollection<AlbumOverrideRowViewModel> AlbumOverrides { get; } = [];

    /// <summary>
    /// 作品の作曲家に選べる正規形。**自由入力させないための選択肢**（docs/SPEC.md 7.3.1）。
    /// 作曲家の編集にあわせて作り直す。
    /// </summary>
    public ObservableCollection<string> ComposerCanonicals { get; } = [];

    /// <summary>楽語の誤記。</summary>
    public ObservableCollection<TypoRowViewModel> Typos { get; } = [];

    /// <summary>保護対象の <c>albumartist</c>。</summary>
    public ObservableCollection<ProtectedValueRowViewModel> ProtectedValues { get; } = [];

    /// <summary>検証で見つかった問題。**エラーは保存を止める。**</summary>
    public ObservableCollection<DictionaryIssue> Issues { get; } = [];

    /// <summary>辞書ファイルのパス。</summary>
    public string FilePath => _store.FilePath;

    /// <summary>
    /// 同梱の既定辞書から取り込める件数。
    ///
    /// 起動時に数えておく。**気づけないと、アプリ側でルールの誤検出を直しても
    /// 既存の利用者には届かない。** 実際に段階 7 の <c>noConductor</c> がそうなっていた。
    /// </summary>
    [ObservableProperty]
    private int _pendingMergeCount;

    /// <summary>
    /// 同梱の既定辞書との差分を数える。**比較に失敗しても起動は妨げない。**
    /// </summary>
    private void CheckForUpdates()
    {
        try
        {
            PendingMergeCount = _store.BuildMergePlan().Count;

            if (PendingMergeCount > 0)
            {
                StatusText = $"既定辞書に {PendingMergeCount:N0} 件の更新があります。「既定辞書から取り込む」で反映できます。";
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "既定辞書との比較に失敗した");
        }
    }

    /// <summary>
    /// ストアの内容を読み直して編集行を作り直す。
    /// 検査結果からの追加のように、辞書タブの外で辞書が変わったときに呼ぶ。
    /// </summary>
    public void ReloadFromStore()
    {
        Load();
    }

    /// <summary>
    /// 未保存の変更を確認する。閉じる前や他の操作に移る前に呼ぶ。
    /// </summary>
    /// <returns>続行してよければ true。</returns>
    public bool ConfirmDiscardIfDirty()
    {
        if (!IsDirty)
        {
            return true;
        }

        return MessageBox.Show(
            "辞書に未保存の変更があります。破棄して続けますか？",
            "未保存の変更",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;
    }

    /// <summary>
    /// 辞書を保存する。検証でエラーが出た場合は保存しない。
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Save()
    {
        TagDictionary edited = BuildDictionary();

        RefreshIssues(edited);

        if (DictionaryValidator.HasError(Issues))
        {
            StatusText = $"エラーが {Issues.Count(issue => issue.Severity == DictionaryIssueSeverity.Error)} 件あります。修正するまで保存できません。";

            MessageBox.Show(
                "辞書に保存できない問題があります。下の一覧を確認してください。"
                + Environment.NewLine + Environment.NewLine
                + "特に「別名が既に使われています」は、そのまま保存しても照合に載らず"
                + "登録した意味がなくなるため止めています。",
                "辞書を保存できません",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        try
        {
            _store.Save(edited);

            IsDirty = false;
            SummaryText = DictionarySummary.Describe(edited);

            StatusText = Issues.Count == 0
                ? $"保存しました: {FilePath}"
                : $"保存しました（警告 {Issues.Count} 件）: {FilePath}";

            Log.Information(
                "辞書を保存した path={Path} {Summary} 警告={Warnings}",
                FilePath,
                DictionarySummary.Describe(edited),
                Issues.Count);

            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText = $"保存に失敗しました: {ex.Message}";
            Log.Error(ex, "辞書の保存に失敗した path={Path}", FilePath);
        }
    }

    /// <summary>
    /// 編集を破棄して保存済みの内容に戻す。
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsDirty))]
    private void Revert()
    {
        if (!ConfirmDiscardIfDirty())
        {
            return;
        }

        _store.Reload();
        Load();

        StatusText = "保存済みの辞書を読み直しました。";
    }

    /// <summary>
    /// 現在の辞書を既定辞書として書き出す。
    ///
    /// 利用者辞書は <c>%APPDATA%</c> にあり、リポジトリ同梱の既定辞書とは別物である。
    /// 育てた内容を同梱側へ戻すための導線（docs/SPEC.md 13章 D5）。
    /// </summary>
    [RelayCommand]
    private void Export()
    {
        if (IsDirty)
        {
            MessageBox.Show(
                "未保存の変更があります。先に保存してから書き出してください。",
                "書き出せません",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        string? bundledPath = AppConst.FindBundledDictionaryPath();

        SaveFileDialog dialog = new()
        {
            Title = "既定辞書として書き出す",
            FileName = bundledPath is null ? AppConst.BUNDLED_DICTIONARY_FILE_NAME : Path.GetFileName(bundledPath),
            InitialDirectory = bundledPath is null ? string.Empty : Path.GetDirectoryName(bundledPath),
            Filter = "JSON ファイル|*.json",
            DefaultExt = ".json",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _store.Export(dialog.FileName);

            StatusText = $"既定辞書として書き出しました: {dialog.FileName}";
            Log.Information("既定辞書を書き出した path={Path}", dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusText = $"書き出しに失敗しました: {ex.Message}";
            Log.Error(ex, "既定辞書の書き出しに失敗した path={Path}", dialog.FileName);
        }
    }

    /// <summary>
    /// 同梱の既定辞書から差分を取り込む。
    ///
    /// 利用者辞書は初回起動時にコピーされたきり更新されないため、アプリ側に足した
    /// エントリや設定はこの導線でしか届かない。**取り込む前に必ず一覧を見せる。**
    /// </summary>
    [RelayCommand]
    private void MergeFromDefault()
    {
        if (!ConfirmDiscardIfDirty())
        {
            return;
        }

        IReadOnlyList<DictionaryMergeItem> plan;

        try
        {
            plan = _store.BuildMergePlan();
        }
        catch (Exception ex)
        {
            StatusText = $"既定辞書との比較に失敗しました: {ex.Message}";
            Log.Error(ex, "既定辞書との比較に失敗した");

            return;
        }

        if (plan.Count == 0)
        {
            StatusText = "既定辞書との差分はありません。";
            return;
        }

        MergeDictionaryWindow window = new(plan)
        {
            Owner = Application.Current?.MainWindow,
        };

        if (window.ShowDialog() != true)
        {
            return;
        }

        ApplyMerge([.. window.Items]);
    }

    /// <summary>
    /// 取り込みを実行して保存する。
    /// </summary>
    private void ApplyMerge(IReadOnlyList<DictionaryMergeItem> items)
    {
        try
        {
            TagDictionary merged = _store.Merge(items);

            RefreshIssues(merged);

            if (DictionaryValidator.HasError(Issues))
            {
                MessageBox.Show(
                    "取り込むと辞書に問題が生じます。下の一覧を確認してください。"
                    + Environment.NewLine + Environment.NewLine
                    + "取り込みは中止しました。",
                    "取り込めません",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            _store.Save(merged);
            Load();

            PendingMergeCount = _store.BuildMergePlan().Count;

            int applied = items.Count(item => item.IsSelected);

            StatusText = $"既定辞書から {applied:N0} 件を取り込みました。";
            Log.Information("既定辞書から取り込んだ 件数={Count} 版={Version}", applied, merged.Version);

            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusText = $"取り込みに失敗しました: {ex.Message}";
            Log.Error(ex, "既定辞書からの取り込みに失敗した");
        }
    }

    /// <summary>
    /// 作曲家を追加する。
    /// </summary>
    [RelayCommand]
    private void AddComposer()
    {
        ComposerRowViewModel row = new(new ComposerEntry { Canonical = "新しい作曲家" });

        Composers.Add(row);
        SelectedComposer = row;
    }

    /// <summary>
    /// 作曲家を削除する。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveComposer))]
    private void RemoveComposer()
    {
        if (SelectedComposer is null || !ConfirmRemove(DictionaryValidator.CATEGORY_COMPOSER, SelectedComposer.Canonical))
        {
            return;
        }

        Composers.Remove(SelectedComposer);
    }

    /// <summary>
    /// 人物を追加する。
    /// </summary>
    [RelayCommand]
    private void AddPerson()
    {
        PersonRowViewModel row = new(new PersonEntry
        {
            Canonical = "新しい人物",
            Roles = [nameof(PersonRole.Conductor)],
        });

        Persons.Add(row);
        SelectedPerson = row;
    }

    /// <summary>
    /// 人物を削除する。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemovePerson))]
    private void RemovePerson()
    {
        if (SelectedPerson is null || !ConfirmRemove(DictionaryValidator.CATEGORY_PERSON, SelectedPerson.Canonical))
        {
            return;
        }

        Persons.Remove(SelectedPerson);
    }

    /// <summary>
    /// 団体を追加する。
    /// </summary>
    [RelayCommand]
    private void AddEnsemble()
    {
        EnsembleRowViewModel row = new(new EnsembleEntry
        {
            EntityId = DictionaryEditor.SuggestEntityId(BuildDictionary(), "new-ensemble"),
            Canonical = "新しい団体",
        });

        Ensembles.Add(row);
        SelectedEnsemble = row;
    }

    /// <summary>
    /// 団体を削除する。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveEnsemble))]
    private void RemoveEnsemble()
    {
        if (SelectedEnsemble is null || !ConfirmRemove(DictionaryValidator.CATEGORY_ENSEMBLE, SelectedEnsemble.DisplayName))
        {
            return;
        }

        Ensembles.Remove(SelectedEnsemble);
    }

    /// <summary>
    /// 選択中の団体に時代区分を追加する。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveEnsemble))]
    private void AddEra()
    {
        SelectedEnsemble?.Eras.Add(new EnsembleEraRowViewModel(new EnsembleEra { Canonical = string.Empty }));
    }

    /// <summary>
    /// 選択中の時代区分を削除する。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveEra))]
    private void RemoveEra()
    {
        if (SelectedEnsemble is null || SelectedEra is null)
        {
            return;
        }

        SelectedEnsemble.Eras.Remove(SelectedEra);
    }

    /// <summary>
    /// 作品を追加する（docs/SPEC.md 7.3.1）。
    ///
    /// 作曲家は選択中のものを引き継ぐ。**空のままにはできない**ので、
    /// 何も選ばれていなければ辞書の先頭の作曲家を入れておく（7.4.3 の同定キーの一部）。
    /// </summary>
    [RelayCommand]
    private void AddWork()
    {
        WorkRowViewModel row = new(new WorkEntry
        {
            Composer = SelectedWork?.Composer is { Length: > 0 } composer
                ? composer
                : ComposerCanonicals.FirstOrDefault() ?? string.Empty,
            Canonical = string.Empty,
        });

        Works.Add(row);
        SelectedWork = row;
    }

    /// <summary>
    /// 作品を削除する。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveWork))]
    private void RemoveWork()
    {
        if (SelectedWork is null || !ConfirmRemove(DictionaryValidator.CATEGORY_WORK, SelectedWork.DisplayName))
        {
            return;
        }

        Works.Remove(SelectedWork);
    }

    /// <summary>
    /// 個別例外を削除する。
    ///
    /// **追加の入口はここに置かない。** フォルダは検査結果から埋める（docs/SPEC.md 7.3.2）。
    /// 手で相対パスを打つと、打ち間違えても例外が黙って効かなくなるだけで原因が画面から分からない。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveAlbumOverride))]
    private void RemoveAlbumOverride()
    {
        if (SelectedAlbumOverride is null
            || !ConfirmRemove(DictionaryValidator.CATEGORY_OVERRIDE, SelectedAlbumOverride.Folder))
        {
            return;
        }

        AlbumOverrides.Remove(SelectedAlbumOverride);
    }

    /// <summary>
    /// 誤記を追加する。
    /// </summary>
    [RelayCommand]
    private void AddTypo()
    {
        // パターンは空で作る。検証がエラーとして拾うので、埋め忘れたまま保存されることはない。
        TypoRowViewModel row = new(new TypoEntry { Pattern = string.Empty, Replacement = string.Empty });

        Typos.Add(row);
        SelectedTypo = row;
    }

    /// <summary>
    /// 誤記を削除する。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveTypo))]
    private void RemoveTypo()
    {
        if (SelectedTypo is null || !ConfirmRemove(DictionaryValidator.CATEGORY_TYPO, SelectedTypo.Pattern))
        {
            return;
        }

        Typos.Remove(SelectedTypo);
    }

    /// <summary>
    /// 保護対象を追加する。
    /// </summary>
    [RelayCommand]
    private void AddProtectedValue()
    {
        ProtectedValueRowViewModel row = new(string.Empty);

        ProtectedValues.Add(row);
        SelectedProtectedValue = row;
    }

    /// <summary>
    /// 保護対象を削除する。
    ///
    /// **配役情報の保護を外すと R-207 / R-208 が誤検出だらけになる**ため、
    /// 通常の削除より強い確認を出す（docs/library-baseline-2026-08-03.md）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveProtectedValue))]
    private void RemoveProtectedValue()
    {
        if (SelectedProtectedValue is null)
        {
            return;
        }

        bool confirmed = MessageBox.Show(
            $"保護対象から次の値を外します。{Environment.NewLine}{Environment.NewLine}"
            + $"{SelectedProtectedValue.Value}{Environment.NewLine}{Environment.NewLine}"
            + "保護対象は配役情報を含むため書き換えない値です（TAGGING_POLICY 2.3）。"
            + "外すと検査の対象に戻り、楽団名に縮める修正案が出るようになります。",
            "保護対象から外しますか？",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;

        if (!confirmed)
        {
            return;
        }

        ProtectedValues.Remove(SelectedProtectedValue);
    }

    /// <summary>
    /// 現在の編集内容を検証し、保存せずに問題だけを出す。
    /// </summary>
    [RelayCommand]
    private void Validate()
    {
        RefreshIssues(BuildDictionary());

        StatusText = Issues.Count == 0
            ? "問題は見つかりませんでした。"
            : string.Create(
                CultureInfo.CurrentCulture,
                $"エラー {Issues.Count(issue => issue.Severity == DictionaryIssueSeverity.Error):N0} 件 /"
                + $" 警告 {Issues.Count(issue => issue.Severity == DictionaryIssueSeverity.Warning):N0} 件。");
    }

    /// <summary>
    /// 検証結果の表示を閉じる。
    ///
    /// **消えるのは表示だけ。** 検証は保存と「検証だけ実行」のたびにやり直すので、
    /// 問題が残っていれば同じものがまた出る。エラーがあれば保存も止まったままである。
    ///
    /// 警告だけの状態（既定辞書に元からある冗長な別名など）では、直しようがないまま
    /// 一覧の下に居座り、編集領域の高さを取り続ける。閉じる手段が無いのは邪魔でしかない。
    /// </summary>
    [RelayCommand]
    private void DismissIssues()
    {
        Issues.Clear();

        StatusText = "検証結果の表示を閉じました。「検証だけ実行」でいつでも出し直せます。";
    }

    /// <summary>作曲家を削除できるか。</summary>
    private bool CanRemoveComposer()
    {
        return SelectedComposer is not null;
    }

    /// <summary>人物を削除できるか。</summary>
    private bool CanRemovePerson()
    {
        return SelectedPerson is not null;
    }

    /// <summary>団体を削除できるか。</summary>
    private bool CanRemoveEnsemble()
    {
        return SelectedEnsemble is not null;
    }

    /// <summary>時代区分を削除できるか。</summary>
    private bool CanRemoveEra()
    {
        return SelectedEnsemble is not null && SelectedEra is not null;
    }

    /// <summary>作品を削除できるか。</summary>
    private bool CanRemoveWork()
    {
        return SelectedWork is not null;
    }

    /// <summary>個別例外を削除できるか。</summary>
    private bool CanRemoveAlbumOverride()
    {
        return SelectedAlbumOverride is not null;
    }

    /// <summary>誤記を削除できるか。</summary>
    private bool CanRemoveTypo()
    {
        return SelectedTypo is not null;
    }

    /// <summary>保護対象を削除できるか。</summary>
    private bool CanRemoveProtectedValue()
    {
        return SelectedProtectedValue is not null;
    }

    /// <summary>
    /// 削除してよいかを確認する。
    /// </summary>
    private static bool ConfirmRemove(string category, string name)
    {
        return MessageBox.Show(
            $"{category}「{name}」を辞書から削除します。"
            + Environment.NewLine + Environment.NewLine
            + "削除すると、この名前に対する修正案は出なくなります。",
            "辞書から削除しますか？",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel) == MessageBoxResult.OK;
    }

    /// <summary>
    /// 絞り込み文字列が変わったら一覧を絞り直す。
    /// </summary>
    partial void OnFilterTextChanged(string value)
    {
        foreach (ICollectionView view in _views)
        {
            view.Refresh();
        }
    }

    /// <summary>
    /// 誤記のテスト欄を更新する。
    /// </summary>
    partial void OnTypoTestInputChanged(string value)
    {
        UpdateTypoTest();
    }

    /// <summary>
    /// 選択中の誤記が変わったらテスト結果を出し直す。
    /// </summary>
    partial void OnSelectedTypoChanged(TypoRowViewModel? value)
    {
        UpdateTypoTest();
    }

    /// <summary>
    /// 選択中のパターンをテスト文字列に当てて結果を出す（docs/SPEC.md 7.3）。
    /// </summary>
    private void UpdateTypoTest()
    {
        if (SelectedTypo is null)
        {
            TypoTestResult = "誤記を選ぶと、その場で試せます。";
            return;
        }

        if (!SelectedTypo.IsValidPattern)
        {
            TypoTestResult = "パターンが正規表現として不正です。";
            return;
        }

        if (TypoTestInput.Length == 0)
        {
            TypoTestResult = "文字列を入れると、選択中のパターンで試した結果が出ます。";
            return;
        }

        TagDictionary probe = new() { Typos = [SelectedTypo.ToEntry()] };
        DictionaryIndex index = new(probe);

        bool matched = index.FindTypos(TypoTestInput).Count > 0;

        TypoTestResult = matched
            ? $"一致しました → 「{index.ApplyTypoFixes(TypoTestInput)}」"
            : "一致しませんでした。";
    }

    /// <summary>
    /// ストアの内容を編集行に展開する。
    /// </summary>
    private void Load()
    {
        _isLoading = true;

        Detach();

        Composers.Clear();
        Persons.Clear();
        Ensembles.Clear();
        Works.Clear();
        AlbumOverrides.Clear();
        Typos.Clear();
        ProtectedValues.Clear();
        Issues.Clear();

        TagDictionary dictionary = _store.Dictionary;

        foreach (ComposerEntry entry in dictionary.Composers ?? [])
        {
            Composers.Add(new ComposerRowViewModel(entry));
        }

        foreach (PersonEntry entry in dictionary.Persons ?? [])
        {
            Persons.Add(new PersonRowViewModel(entry));
        }

        foreach (EnsembleEntry entry in dictionary.Ensembles ?? [])
        {
            Ensembles.Add(new EnsembleRowViewModel(entry));
        }

        foreach (WorkEntry entry in dictionary.Works ?? [])
        {
            Works.Add(new WorkRowViewModel(entry));
        }

        foreach (AlbumOverrideEntry entry in dictionary.AlbumOverrides ?? [])
        {
            AlbumOverrides.Add(new AlbumOverrideRowViewModel(entry));
        }

        foreach (TypoEntry entry in dictionary.Typos ?? [])
        {
            Typos.Add(new TypoRowViewModel(entry));
        }

        foreach (string value in dictionary.ProtectedAlbumArtists ?? [])
        {
            ProtectedValues.Add(new ProtectedValueRowViewModel(value));
        }

        RefreshComposerCanonicals();
        SummaryText = DictionarySummary.Describe(dictionary);

        Attach();
        SetUpViews();

        IsDirty = false;
        _isLoading = false;
    }

    /// <summary>
    /// 絞り込み用のビューを用意する。初回だけ作ればよい。
    /// </summary>
    private void SetUpViews()
    {
        if (_views.Count > 0)
        {
            return;
        }

        AddView(Composers);
        AddView(Persons);
        AddView(Ensembles);
        AddView(Works);
        AddView(AlbumOverrides);
        AddView(Typos);
        AddView(ProtectedValues);
    }

    /// <summary>
    /// 作品の作曲家に選べる正規形を作り直す（docs/SPEC.md 7.3.1）。
    ///
    /// **作品が現に名乗っている作曲家も候補に残す。** 候補から消えると選択欄は「選択なし」に
    /// 落ち、編集していない行の作曲家まで黙って失われる。正規形と一致しない作曲家は
    /// <see cref="DictionaryValidator"/> が保存時にエラーとして出す。
    ///
    /// 入れ替えではなく差分で直す。作り直すたびに選択が外れると、開いている行の値が変わる。
    /// </summary>
    private void RefreshComposerCanonicals()
    {
        string[] desired =
        [
            .. Composers.Select(row => row.Canonical.Trim())
                .Concat(Works.Select(row => row.Composer.Trim()))
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        HashSet<string> keep = new(desired, StringComparer.Ordinal);

        for (int i = ComposerCanonicals.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(ComposerCanonicals[i]))
            {
                ComposerCanonicals.RemoveAt(i);
            }
        }

        for (int i = 0; i < desired.Length; i++)
        {
            if (i >= ComposerCanonicals.Count)
            {
                ComposerCanonicals.Add(desired[i]);
            }
            else if (!string.Equals(ComposerCanonicals[i], desired[i], StringComparison.Ordinal))
            {
                ComposerCanonicals.Insert(i, desired[i]);
            }
        }
    }

    /// <summary>
    /// 絞り込みビューを 1 件登録し、名前の昇順で並べる。
    ///
    /// **並べるのは表示だけで、保存する順序は変えない。**<see cref="BuildDictionary"/> は
    /// コレクションの実体をそのまま書き出す。誤記（<c>typos</c>）は書かれた順に置換を重ねるため、
    /// 表示の都合で並べ替えたものを保存すると置換の結果が変わりうる。JSON の差分も無用に膨らむ。
    ///
    /// 比較は <see cref="NaturalOrder"/> に任せる。<c>SortDescriptions</c> はプロパティの
    /// 既定の比較しか使えず、**番号を数として見られない**（<c>Symphony No. 10</c> が
    /// <c>No. 4</c> より前に来る）。作品名は番号で呼ぶものが大半なので、それでは一覧を
    /// 目で追えない。
    ///
    /// 並べ替えは読み込みと行の増減で効く。**編集中に行が動くことはない**（ライブ整列は入れない）。
    /// 打っている途中で行が跳ねると、どの行を編集していたのか見失う。
    /// </summary>
    /// <param name="source">対象のコレクション。</param>
    private void AddView(System.Collections.IEnumerable source)
    {
        // 対象はすべて ObservableCollection なので、既定のビューは必ず ListCollectionView になる。
        // 取り違えたら並べ替えが黙って効かなくなるだけなので、その場で落として気づけるようにする。
        ListCollectionView view = (ListCollectionView)CollectionViewSource.GetDefaultView(source);

        view.Filter = row => row is IDictionaryRow entry && Matches(entry.SearchText);
        view.CustomSort = NaturalOrderRowComparer.Instance;

        _views.Add(view);
    }

    /// <summary>
    /// 絞り込み文字列に一致するかを判定する。
    /// </summary>
    private bool Matches(string searchText)
    {
        return FilterText.Length == 0
            || searchText.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 編集行の変更を拾えるようにする。
    /// </summary>
    private void Attach()
    {
        Composers.CollectionChanged += OnCollectionChanged;
        Persons.CollectionChanged += OnCollectionChanged;
        Ensembles.CollectionChanged += OnCollectionChanged;
        Works.CollectionChanged += OnCollectionChanged;
        AlbumOverrides.CollectionChanged += OnCollectionChanged;
        Typos.CollectionChanged += OnCollectionChanged;
        ProtectedValues.CollectionChanged += OnCollectionChanged;

        foreach (ObservableObject row in AllRows())
        {
            row.PropertyChanged += OnRowChanged;
        }

        foreach (EnsembleRowViewModel ensemble in Ensembles)
        {
            ensemble.Eras.CollectionChanged += OnCollectionChanged;
        }
    }

    /// <summary>
    /// 変更の購読を外す。
    /// </summary>
    private void Detach()
    {
        Composers.CollectionChanged -= OnCollectionChanged;
        Persons.CollectionChanged -= OnCollectionChanged;
        Ensembles.CollectionChanged -= OnCollectionChanged;
        Works.CollectionChanged -= OnCollectionChanged;
        AlbumOverrides.CollectionChanged -= OnCollectionChanged;
        Typos.CollectionChanged -= OnCollectionChanged;
        ProtectedValues.CollectionChanged -= OnCollectionChanged;

        foreach (ObservableObject row in AllRows())
        {
            row.PropertyChanged -= OnRowChanged;
        }

        foreach (EnsembleRowViewModel ensemble in Ensembles)
        {
            ensemble.Eras.CollectionChanged -= OnCollectionChanged;
        }
    }

    /// <summary>
    /// すべての編集行を列挙する。時代区分も含む。
    /// </summary>
    private IEnumerable<ObservableObject> AllRows()
    {
        return Composers.Cast<ObservableObject>()
            .Concat(Persons)
            .Concat(Ensembles)
            .Concat(Ensembles.SelectMany(ensemble => ensemble.Eras))
            .Concat(Works)
            .Concat(AlbumOverrides)
            .Concat(Typos)
            .Concat(ProtectedValues);
    }

    /// <summary>
    /// 行が増減したら購読を張り直して未保存とする。
    /// </summary>
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (object item in e.NewItems ?? Array.Empty<object>())
        {
            if (item is ObservableObject row)
            {
                row.PropertyChanged += OnRowChanged;
            }

            if (item is EnsembleRowViewModel ensemble)
            {
                ensemble.Eras.CollectionChanged += OnCollectionChanged;
            }
        }

        foreach (object item in e.OldItems ?? Array.Empty<object>())
        {
            if (item is ObservableObject row)
            {
                row.PropertyChanged -= OnRowChanged;
            }

            if (item is EnsembleRowViewModel ensemble)
            {
                ensemble.Eras.CollectionChanged -= OnCollectionChanged;
            }
        }

        if (ReferenceEquals(sender, Composers) || ReferenceEquals(sender, Works))
        {
            RefreshComposerCanonicals();
        }

        MarkDirty();
    }

    /// <summary>
    /// 行の内容が変わったら未保存とする。
    /// 作曲家の正規形は作品の選択肢にもなるので、変わったら候補を作り直す。
    /// </summary>
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ComposerRowViewModel && e.PropertyName == nameof(ComposerRowViewModel.Canonical))
        {
            RefreshComposerCanonicals();
        }

        MarkDirty();
    }

    /// <summary>
    /// 未保存の印を付ける。
    /// </summary>
    private void MarkDirty()
    {
        if (_isLoading)
        {
            return;
        }

        IsDirty = true;
    }

    /// <summary>
    /// 編集行から辞書を組み立てる。
    ///
    /// <c>version</c> と注意書き（<c>_comment</c> 等）は元の辞書から引き継ぐ。
    /// **注意書きを落とすと、辞書を編集する人が前提を知らないまま値を足せるようになる。**
    /// </summary>
    private TagDictionary BuildDictionary()
    {
        return _store.Dictionary with
        {
            Composers = [.. Composers.Select(row => row.ToEntry())],
            Persons = [.. Persons.Select(row => row.ToEntry())],
            Ensembles = [.. Ensembles.Select(row => row.ToEntry())],
            Works = [.. Works.Select(row => row.ToEntry())],
            AlbumOverrides = [.. AlbumOverrides.Select(row => row.ToEntry())],
            Typos = [.. Typos.Select(row => row.ToEntry())],
            ProtectedAlbumArtists = [.. ProtectedValues.Select(row => row.Value.Trim()).Where(value => value.Length > 0)],
        };
    }

    /// <summary>
    /// 検証結果を入れ替える。
    /// </summary>
    private void RefreshIssues(TagDictionary dictionary)
    {
        Issues.Clear();

        foreach (DictionaryIssue issue in DictionaryValidator.Validate(dictionary))
        {
            Issues.Add(issue);
        }
    }
}
