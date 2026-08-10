using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// 検査結果タブ下段の差分明細 1 行。<see cref="TagChange"/> のラッパー。
///
/// **Core のモデルを直接グリッドに束ねないための層。**
/// <see cref="TagChange"/> は Core の純粋な record で <c>INotifyPropertyChanged</c> を持たない。
/// チェックボックスを直接束ねると、値は入るのに通知が起きず、
/// 「チェックした項目を適用」の <c>CanExecute</c> が再評価されない（docs/DEVELOPMENT.md「画面」節）。
///
/// 適用対象の唯一の真実は <see cref="TagChange.IsSelected"/> のままにする。
/// <c>ApplyService</c> と <c>ChangeCsvExporter</c> がそこを読むため、
/// このラッパーは書き込みを素通しさせるだけで、選択状態を自前で持たない。
/// </summary>
public sealed class TagChangeViewModel : ObservableObject
{
    /// <summary>
    /// 修正案からビューモデルを作る。
    /// </summary>
    /// <param name="change">元の修正案。</param>
    public TagChangeViewModel(TagChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        Change = change;

        // フォルダでの絞り込みは明細 1,000 件超を毎回走査する。
        // 相対パスの切り出しは変わらないので、ここで 1 回だけ計算しておく。
        FolderPath = Path.GetDirectoryName(change.RelativePath) ?? string.Empty;
    }

    /// <summary>元の修正案。適用と辞書登録はこちらを渡す。</summary>
    public TagChange Change { get; }

    /// <summary>相対パスのうちフォルダ部分。ツリーでの絞り込みに使う。</summary>
    public string FolderPath { get; }

    /// <summary>
    /// 適用対象にするかどうか（docs/SPEC.md 9章）。
    ///
    /// 既定値は <see cref="TagChange.IsSelected"/> がそのまま返す。
    /// ここで判定条件を書き直すと Core 側とずれるため、読むだけにしてある。
    /// </summary>
    public bool IsSelected
    {
        get => Change.IsSelected;

        set
        {
            if (Change.IsSelected == value)
            {
                return;
            }

            Change.IsSelected = value;
            OnPropertyChanged();
        }
    }

    /// <summary>判定区分。行の色分けに使う。<see cref="IsSelected"/> に依存しないので再通知しない。</summary>
    public string Classification => Change.Classification;

    /// <summary>ライブラリルートからの相対パス。</summary>
    public string RelativePath => Change.RelativePath;

    /// <summary>対象フィールド。</summary>
    public TagField Field => Change.Field;

    /// <summary>表示用の現在値。</summary>
    public string BeforeText => Change.BeforeText;

    /// <summary>表示用の修正後の値。</summary>
    public string AfterText => Change.AfterText;

    /// <summary>判定の根拠。**UI に必ず出す**（docs/SPEC.md 5.3）。</summary>
    public string Rationale => Change.Rationale;

    /// <summary>検出したルールの ID。</summary>
    public string RuleId => Change.RuleId;

    /// <summary>修正値を持つか。持たないものは一覧に出すだけで適用できない。</summary>
    public bool HasFix => Change.HasFix;
}
