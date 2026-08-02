using CommunityToolkit.Mvvm.ComponentModel;
using MusicTagger.Core.Inspection;
using MusicTagger.Core.Models;

namespace MusicTagger.App.ViewModels;

/// <summary>
/// 検査結果タブ上段の 1 行。ルール単位の集計。
/// </summary>
public sealed partial class RuleResultViewModel : ObservableObject
{
    /// <summary>重大度の記号（docs/SPEC.md 6章）。</summary>
    private static readonly Dictionary<Severity, string> SEVERITY_MARKS = new()
    {
        [Severity.Error] = "⛔",
        [Severity.Warning] = "⚠",
        [Severity.Info] = "❓",
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

        // 修正案を持つ ⛔ だけを既定でチェックする。❓ と保留は既定で外す。
        _isSelected = result.Severity == Severity.Error && result.FixableCount > 0;
    }

    /// <summary>元のルール結果。</summary>
    public RuleResult Result { get; }

    /// <summary>ルール ID。</summary>
    public string RuleId => Result.RuleId;

    /// <summary>重大度の記号。</summary>
    public string SeverityMark => SEVERITY_MARKS[Result.Severity];

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
