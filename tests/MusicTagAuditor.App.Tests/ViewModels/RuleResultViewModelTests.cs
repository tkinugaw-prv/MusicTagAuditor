using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// <see cref="RuleResultViewModel"/> のテスト。
///
/// 上段のルール行と下段の明細をつなぐ層。ここが黙ると
/// 「チェックしたのに適用ボタンが灰色のまま」「ルール行を切り替えても
/// 下段の表示が変わらない」が起きる。
/// </summary>
public sealed class RuleResultViewModelTests
{
    /// <summary>
    /// 明細ぶんのビューモデルを持ち、既定のチェック状態が元の修正案と一致する。
    /// </summary>
    [Fact]
    public void 明細のビューモデルを作る()
    {
        RuleResult result = CreateResult(
            Severity.Error,
            CreateChange("01.m4a", Severity.Error, ["Classic"]),
            CreateChange("02.m4a", Severity.Error, []));

        RuleResultViewModel rule = new(result);

        Assert.Equal(2, rule.Changes.Count);
        Assert.True(rule.Changes[0].IsSelected);
        Assert.False(rule.Changes[1].IsSelected);
    }

    /// <summary>
    /// ルール行のチェックを外すと、修正値を持つ明細だけが追従する。
    /// </summary>
    [Fact]
    public void IsSelected_修正値を持つ明細だけを一括で切り替える()
    {
        TagChange fixable = CreateChange("01.m4a", Severity.Error, ["Classic"]);
        TagChange noFix = CreateChange("02.m4a", Severity.Error, []);
        RuleResultViewModel rule = new(CreateResult(Severity.Error, fixable, noFix));

        rule.IsSelected = false;

        Assert.False(fixable.IsSelected);
        Assert.False(rule.Changes[0].IsSelected);

        rule.IsSelected = true;

        Assert.True(fixable.IsSelected);

        // 修正値が無いものはチェックしても適用できないので、一括では触らない。
        Assert.False(noFix.IsSelected);
    }

    /// <summary>
    /// 明細を 1 件チェックすると、ルール経由で親へ伝わる。
    /// これが上がらないと適用ボタンの活性が更新されない。
    /// </summary>
    [Fact]
    public void ChangeSelectionChanged_明細のチェックで発火する()
    {
        RuleResultViewModel rule = new(
            CreateResult(Severity.Info, CreateChange("01.m4a", Severity.Info, ["Langsam"])));

        int raised = 0;
        rule.ChangeSelectionChanged += (_, _) => raised++;

        rule.Changes[0].IsSelected = true;

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// ルール行の一括反映でも親へ伝わる。
    /// </summary>
    [Fact]
    public void ChangeSelectionChanged_ルール行の一括反映でも発火する()
    {
        RuleResultViewModel rule = new(
            CreateResult(Severity.Error, CreateChange("01.m4a", Severity.Error, ["Classic"])));

        int raised = 0;
        rule.ChangeSelectionChanged += (_, _) => raised++;

        rule.IsSelected = false;

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// テスト用のルール結果を作る。
    /// </summary>
    /// <param name="severity">重大度。</param>
    /// <param name="changes">検出した修正案。</param>
    /// <returns>ルール結果。</returns>
    private static RuleResult CreateResult(Severity severity, params TagChange[] changes)
    {
        return new RuleResult("R-102", severity, "genre は Classic に統一する", changes);
    }

    /// <summary>
    /// テスト用の修正案を作る。
    /// </summary>
    /// <param name="relativePath">相対パス。</param>
    /// <param name="severity">重大度。</param>
    /// <param name="after">修正後の値。空なら修正値なし。</param>
    /// <returns>修正案。</returns>
    private static TagChange CreateChange(
        string relativePath,
        Severity severity,
        IReadOnlyList<string> after)
    {
        return new TagChange(
            relativePath,
            TagField.Genre,
            ["Classical"],
            after,
            "R-102",
            "genre は Classic に統一する",
            severity);
    }
}
