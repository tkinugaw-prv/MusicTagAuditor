using System.ComponentModel;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// <see cref="TagChangeViewModel"/> のテスト。
///
/// 守りたいのは 2 点。
/// 1. チェックの既定値を Core と二重に書かないこと（ずれると
///    「ルール行はチェック済みなのに明細は未チェック」が起きる）。
/// 2. チェックの変化が通知されること。通知が無いと
///    「チェックした項目を適用」の CanExecute が再評価されず、ボタンが灰色のまま固まる。
/// </summary>
public sealed class TagChangeViewModelTests
{
    /// <summary>
    /// 修正値が決まっている ⛔ は既定でチェック済みになる。
    /// </summary>
    [Fact]
    public void 既定値_修正値のあるエラーはチェック済み()
    {
        TagChangeViewModel change = new(CreateChange(Severity.Error, after: ["Classic"]));

        Assert.True(change.IsSelected);
        Assert.True(change.Change.IsSelected);
    }

    /// <summary>
    /// ❓ は修正値があっても既定で外れる（docs/SPEC.md 9.1）。
    /// </summary>
    [Fact]
    public void 既定値_要確認は修正値があっても未チェック()
    {
        TagChangeViewModel change = new(CreateChange(Severity.Info, after: ["Classic"]));

        Assert.False(change.IsSelected);
    }

    /// <summary>
    /// 保留は重大度によらず既定で外れる。
    /// </summary>
    [Fact]
    public void 既定値_保留は未チェック()
    {
        TagChangeViewModel change = new(
            CreateChange(Severity.Error, after: ["Classic"], hold: HoldReason.EraUnknown));

        Assert.False(change.IsSelected);
    }

    /// <summary>
    /// チェックすると Core 側へ素通しされ、通知が上がる。
    /// 適用対象を決めるのは Core の <see cref="TagChange.IsSelected"/> なので、
    /// 素通しが切れると画面のチェックと書き込み対象がずれる。
    /// </summary>
    [Fact]
    public void IsSelected_Coreへ素通しして通知する()
    {
        TagChange source = CreateChange(Severity.Info, after: ["Classic"]);
        TagChangeViewModel change = new(source);
        List<string?> raised = Watch(change);

        change.IsSelected = true;

        Assert.True(source.IsSelected);
        Assert.Equal([nameof(TagChangeViewModel.IsSelected)], raised);
    }

    /// <summary>
    /// 同じ値を入れ直しても通知しない。
    /// ルール行の一括反映は明細 1 件ごとに走るため、素通しすると
    /// 変化していない行の分まで集計が走る。
    /// </summary>
    [Fact]
    public void IsSelected_同じ値なら通知しない()
    {
        TagChangeViewModel change = new(CreateChange(Severity.Error, after: ["Classic"]));
        List<string?> raised = Watch(change);

        change.IsSelected = true;

        Assert.Empty(raised);
    }

    /// <summary>
    /// 表示用の値が元の修正案をそのまま返す。
    /// </summary>
    [Fact]
    public void 表示用の値が元の修正案と一致する()
    {
        TagChange source = CreateChange(Severity.Error, after: ["Classic"]);
        TagChangeViewModel change = new(source);

        Assert.Equal(source.RelativePath, change.RelativePath);
        Assert.Equal(source.Field, change.Field);
        Assert.Equal(source.BeforeText, change.BeforeText);
        Assert.Equal(source.AfterText, change.AfterText);
        Assert.Equal(source.Rationale, change.Rationale);
        Assert.Equal(source.RuleId, change.RuleId);
        Assert.Equal(source.Classification, change.Classification);
        Assert.Equal(source.HasFix, change.HasFix);
    }

    /// <summary>
    /// 上がったプロパティ名を集める。
    /// </summary>
    /// <param name="change">監視するビューモデル。</param>
    /// <returns>通知されたプロパティ名の一覧。</returns>
    private static List<string?> Watch(TagChangeViewModel change)
    {
        List<string?> raised = [];
        change.PropertyChanged += (object? _, PropertyChangedEventArgs e) => raised.Add(e.PropertyName);

        return raised;
    }

    /// <summary>
    /// テスト用の修正案を作る。
    /// </summary>
    /// <param name="severity">重大度。</param>
    /// <param name="after">修正後の値。</param>
    /// <param name="hold">保留の理由。</param>
    /// <returns>修正案。</returns>
    private static TagChange CreateChange(
        Severity severity,
        IReadOnlyList<string> after,
        HoldReason hold = HoldReason.None)
    {
        return new TagChange(
            "ブルックナー\\01.m4a",
            TagField.Genre,
            ["Classical"],
            after,
            "R-102",
            "genre は Classic に統一する",
            severity,
            hold);
    }
}
