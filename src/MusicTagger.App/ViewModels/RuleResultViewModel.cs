using CommunityToolkit.Mvvm.ComponentModel;
using MusicTagger.Core.Inspection;
using MusicTagger.Core.Models;

namespace MusicTagger.App.ViewModels;

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

    /// <summary>
    /// ルールの結果からビューモデルを作る。
    /// </summary>
    /// <param name="result">ルールの結果。</param>
    public RuleResultViewModel(RuleResult result)
    {
        Result = result;

        // 修正値が決まっている ⛔ と ⚠ を既定でチェックする。❓ と保留は既定で外す。
        // TagChange.IsSelected の既定値と同じ条件にすること。ずれると
        // 「ルール行はチェック済みなのに明細は未チェック」という状態になる。
        _isSelected = result.Severity != Severity.Info && result.FixableCount > 0;
    }

    /// <summary>元のルール結果。</summary>
    public RuleResult Result { get; }

    /// <summary>ルール ID。</summary>
    public string RuleId => Result.RuleId;

    /// <summary>重大度。表示色の切り替えと並べ替えに使う。</summary>
    public Severity Severity => Result.Severity;

    /// <summary>重大度の表示名。</summary>
    public string SeverityLabel => SEVERITY_LABELS[Result.Severity];

    /// <summary>説明。</summary>
    public string Description => Result.Description;

    /// <summary>検出件数。</summary>
    public int Count => Result.Changes.Count;

    /// <summary>自動修正できる件数。</summary>
    public int FixableCount => Result.FixableCount;

    /// <summary>保留になった件数。</summary>
    public int HoldCount => Result.HoldCount;

    /// <summary>
    /// このルールの検出をすべて選択／解除する。
    /// 修正案を持たないものはチェックしても適用できないため対象外にする。
    /// </summary>
    partial void OnIsSelectedChanged(bool value)
    {
        foreach (TagChange change in Result.Changes.Where(change => change.HasFix))
        {
            change.IsSelected = value;
        }
    }
}
