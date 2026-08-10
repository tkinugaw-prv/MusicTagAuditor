using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// 検査結果タブ上段の 1 行。ルール単位の集計。
/// </summary>
public sealed partial class RuleResultViewModel : ObservableObject
{
    /// <summary>
    /// 重大度の表示名（docs/SPEC.md 6章）。
    ///
    /// SPEC が記号（⛔ ⚠ ❓）で書いているものを文字にしてある。実描画では単色の代替字形に
    /// 置き換わり、塗りと線の違いしか出ないため意味が読めない。色は XAML 側で付ける。
    /// </summary>
    private static readonly Dictionary<Severity, string> SEVERITY_LABELS = new()
    {
        [Severity.Error] = "エラー",
        [Severity.Warning] = "警告",
        [Severity.Info] = "要確認",

        // 手編集はルールの結果として現れないが、表を引けない重大度を残さない。
        [Severity.Manual] = "手編集",
    };

    /// <summary>このルールを一括で選択するか。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>配下から逆算してヘッダーを直している最中か。配下への撃ち返しを止める。</summary>
    private bool _syncingHeader;

    /// <summary>
    /// 配下を一括で書き換えている最中か。1 件ごとのヘッダー再計算と親への通知を止める。
    /// </summary>
    private bool _isBulkUpdating;

    /// <summary>一括で書き換えている最中にチェックが動いたか。まとめた通知を出すかの判断に使う。</summary>
    private bool _changedDuringBulkUpdate;

    /// <summary>範囲内の明細のうち修正案を持つもの。<see cref="SetScope"/> で作り直す。</summary>
    private TagChangeViewModel[] _scopedFixableChanges = [];

    /// <summary>範囲内の保留件数。バインドのたびに数え直さないよう持っておく。</summary>
    private int _scopedHoldCount;

    /// <summary>
    /// ルールの結果からビューモデルを作る。
    /// </summary>
    /// <param name="result">ルールの結果。</param>
    public RuleResultViewModel(RuleResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Result = result;
        Changes = [.. result.Changes.Select(change => new TagChangeViewModel(change))];

        foreach (TagChangeViewModel change in Changes)
        {
            change.PropertyChanged += OnChangePropertyChanged;
        }

        // 絞り込み前の状態から始める。ヘッダーの既定はこの下で別に決めるので、
        // ここで SyncHeaderFromChanges を走らせないよう素で組み立てる。
        ScopedChanges = Changes;
        _scopedFixableChanges = [.. Changes.Where(change => change.HasFix)];
        _scopedHoldCount = result.HoldCount;

        // 修正値が決まっている ⛔ と ⚠ を既定でチェックする。❓ と保留は既定で外す。
        // TagChange.IsSelected の既定値と同じ条件にすること。ずれると
        // 「ルール行はチェック済みなのに明細は未チェック」という状態になる。
        _isSelected = result.Severity != Severity.Info && result.FixableCount > 0;
    }

    /// <summary>
    /// このルールの明細のうち、いずれかのチェック状態が変わった。
    ///
    /// 明細は 1,000 件を超えることがあるため、親（<c>MainViewModel</c>）には
    /// ルール単位で 1 本にまとめて伝える。子は自分が生成・所有するので購読が漏れない。
    ///
    /// **一括操作では最後に 1 回だけ上げる。** 受け手は件数の数え直しと表示の作り直しを
    /// 行うので、1,000 件の書き換えで 1,000 回上げると画面が固まる。
    /// </summary>
    public event EventHandler? ChangeSelectionChanged;

    /// <summary>元のルール結果。</summary>
    public RuleResult Result { get; }

    /// <summary>このルールの明細の全件。絞り込みの母集合。</summary>
    public IReadOnlyList<TagChangeViewModel> Changes { get; }

    /// <summary>
    /// 絞り込み範囲に入っている明細。下段グリッドはこれを表示する。
    ///
    /// 絞り込みが無いときは <see cref="Changes"/> と同じ内容を指す。
    /// </summary>
    public IReadOnlyList<TagChangeViewModel> ScopedChanges { get; private set; }

    /// <summary>ルール ID。</summary>
    public string RuleId => Result.RuleId;

    /// <summary>重大度。表示色の切り替えと並べ替えに使う。</summary>
    public Severity Severity => Result.Severity;

    /// <summary>重大度の表示名。</summary>
    public string SeverityLabel => SEVERITY_LABELS[Result.Severity];

    /// <summary>説明。</summary>
    public string Description => Result.Description;

    /// <summary>検出件数。絞り込み中は範囲内の件数。</summary>
    public int Count => ScopedChanges.Count;

    /// <summary>自動修正できる件数。絞り込み中は範囲内の件数。</summary>
    public int FixableCount => _scopedFixableChanges.Length;

    /// <summary>保留になった件数。絞り込み中は範囲内の件数。</summary>
    public int HoldCount => _scopedHoldCount;

    /// <summary>
    /// 修正案を持つ明細。チェックしても適用できないものは一括操作の対象外にする。
    ///
    /// **絞り込み範囲の外は含めない。** 一括操作とヘッダー同期はここを通るので、
    /// 範囲外を混ぜると画面に出ていない明細のチェックまで動いてしまう。
    /// </summary>
    public IEnumerable<TagChangeViewModel> FixableChanges => _scopedFixableChanges;

    /// <summary>
    /// 適用できる明細を持つか。
    ///
    /// 持たないルールはチェックしても書き込むものが無いので、画面のチェックボックスを
    /// 無効にする。**チェックできるのに適用されない状態を作らない。**
    /// 選択件数の表示と実際の適用件数が食い違う原因になる。
    /// </summary>
    public bool HasFixableChanges => FixableCount > 0;

    /// <summary>
    /// 絞り込みの範囲を差し替える。
    ///
    /// **ビューモデルを作り直さずに範囲だけ入れ替える。** 作り直すとヘッダーのチェックが
    /// 既定値に戻り、利用者が組み立てた選択が消える。購読と選択中ルールの同一性も切れる。
    /// </summary>
    /// <param name="scope">範囲内なら true を返す判定。null なら全件を範囲とする。</param>
    public void SetScope(Func<TagChangeViewModel, bool>? scope)
    {
        ScopedChanges = scope is null ? Changes : [.. Changes.Where(scope)];
        _scopedFixableChanges = [.. ScopedChanges.Where(change => change.HasFix)];
        _scopedHoldCount = ScopedChanges.Count(change => change.Change.HoldReason != HoldReason.None);

        OnPropertyChanged(nameof(ScopedChanges));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(FixableCount));
        OnPropertyChanged(nameof(HoldCount));
        OnPropertyChanged(nameof(FixableChanges));
        OnPropertyChanged(nameof(HasFixableChanges));

        // 範囲が変われば「全件チェック済みか」の答えも変わる。範囲内の実態から決め直す。
        SyncHeaderFromChanges();
    }

    /// <summary>
    /// 配下のチェックをまとめて書き換える。全選択・全解除・選択反転の入口。
    ///
    /// **ヘッダーの同期と親への通知は最後に 1 回だけ行う。** 1 件ごとに配下を数え直すと、
    /// 明細が 1,000 件を超えるルールで O(n²) になる。
    /// </summary>
    /// <param name="next">今のチェック状態から次の状態を決める。</param>
    public void UpdateChangeSelection(Func<bool, bool> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        BulkUpdateChanges(next);
    }

    /// <summary>
    /// このルールの検出をすべて選択／解除する。
    /// 修正案を持たないものはチェックしても適用できないため対象外にする。
    ///
    /// **反映先は子のビューモデルにする。** <see cref="TagChange"/> を直接書くと
    /// 通知が起きず、表示中の下段グリッドがルールを選び直すまで古いままになる。
    /// </summary>
    partial void OnIsSelectedChanged(bool value)
    {
        // 配下から逆算してヘッダーを直している最中。ここで配下へ撃ち返すと、
        // 「1 件だけ外した」が「全件外す」に化ける。
        if (_syncingHeader)
        {
            return;
        }

        BulkUpdateChanges(_ => value);
    }

    /// <summary>
    /// 配下のチェックを一括で書き換える。
    ///
    /// **1 件ごとに数え直さない。** ヘッダーの同期も親への通知も最後に 1 回だけ行う。
    /// 明細が 1,000 件を超えるルールでは、1 件ごとに通知すると受け手が件数を数え直し、
    /// 表示も作り直すため画面が固まる。
    /// </summary>
    /// <param name="next">今のチェック状態から次の状態を決める。</param>
    private void BulkUpdateChanges(Func<bool, bool> next)
    {
        _isBulkUpdating = true;
        _changedDuringBulkUpdate = false;

        try
        {
            foreach (TagChangeViewModel change in FixableChanges)
            {
                change.IsSelected = next(change.IsSelected);
            }
        }
        finally
        {
            _isBulkUpdating = false;
        }

        SyncHeaderFromChanges();

        // 1 件も動かなかったのなら通知しない。押しても何も変わらない操作で
        // 表示を作り直すと、下段の現在行やスクロール位置が無駄に飛ぶ。
        if (_changedDuringBulkUpdate)
        {
            ChangeSelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 明細のチェック状態の変化を親へ中継する。
    /// </summary>
    private void OnChangePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TagChangeViewModel.IsSelected))
        {
            return;
        }

        // 一括操作の最中。数え直しも通知も BulkUpdateChanges が最後にまとめる。
        if (_isBulkUpdating)
        {
            _changedDuringBulkUpdate = true;
            return;
        }

        SyncHeaderFromChanges();

        ChangeSelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 配下の実態からヘッダーのチェックを決め直す。
    ///
    /// **ヘッダーは配下を一括で切り替えるスイッチだが、配下だけが変わったときに
    /// 取り残されると画面が嘘をつく。** 選択反転を押しても上段のチェックが動かず
    /// 「効いていない」ように見えるのはこれが原因だった。
    /// 混在は <c>bool</c> で表せないので、全件チェック済みのときだけ true にする。
    /// </summary>
    private void SyncHeaderFromChanges()
    {
        if (_scopedFixableChanges.Length == 0)
        {
            return;
        }

        bool allSelected = Array.TrueForAll(_scopedFixableChanges, change => change.IsSelected);

        if (IsSelected == allSelected)
        {
            return;
        }

        _syncingHeader = true;

        try
        {
            IsSelected = allSelected;
        }
        finally
        {
            _syncingHeader = false;
        }
    }
}
