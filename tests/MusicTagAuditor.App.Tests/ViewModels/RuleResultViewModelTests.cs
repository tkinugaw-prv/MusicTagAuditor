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
    /// 修正案を 1 件も持たないルールは、チェックできないものとして扱う。
    /// 画面のチェックボックスを無効にする判断に使う。
    /// </summary>
    [Fact]
    public void HasFixableChanges_修正案が無ければfalse()
    {
        RuleResultViewModel noFix = new(
            CreateResult(Severity.Info, CreateChange("01.m4a", Severity.Info, [])));

        Assert.False(noFix.HasFixableChanges);

        RuleResultViewModel fixable = new(
            CreateResult(Severity.Error, CreateChange("01.m4a", Severity.Error, ["Classic"])));

        Assert.True(fixable.HasFixableChanges);
    }

    /// <summary>
    /// <see cref="RuleResultViewModel.UpdateChangeSelection"/> の反転で、
    /// ヘッダーのチェックが配下の実態に追従する。
    ///
    /// ヘッダーが取り残されると、画面上は「選択反転を押しても何も起きない」ように見える。
    /// </summary>
    [Fact]
    public void UpdateChangeSelection_反転でヘッダーが追従する()
    {
        TagChange first = CreateChange("01.m4a", Severity.Error, ["Classic"]);
        TagChange second = CreateChange("02.m4a", Severity.Error, ["Classic"]);
        RuleResultViewModel rule = new(CreateResult(Severity.Error, first, second));

        Assert.True(rule.IsSelected);

        rule.UpdateChangeSelection(selected => !selected);

        Assert.False(first.IsSelected);
        Assert.False(second.IsSelected);
        Assert.False(rule.IsSelected);

        rule.UpdateChangeSelection(selected => !selected);

        Assert.True(first.IsSelected);
        Assert.True(second.IsSelected);
        Assert.True(rule.IsSelected);
    }

    /// <summary>
    /// 明細を 1 件外すとヘッダーは外れるが、**残りの明細は巻き込まれない**。
    ///
    /// ヘッダー同期が配下へ撃ち返すと「1 件外した」が「全件外す」に化ける。
    /// </summary>
    [Fact]
    public void ヘッダー同期は配下へ撃ち返さない()
    {
        TagChange first = CreateChange("01.m4a", Severity.Error, ["Classic"]);
        TagChange second = CreateChange("02.m4a", Severity.Error, ["Classic"]);
        RuleResultViewModel rule = new(CreateResult(Severity.Error, first, second));

        rule.Changes[0].IsSelected = false;

        Assert.False(rule.IsSelected);
        Assert.False(first.IsSelected);

        // 残りは選択されたまま。
        Assert.True(second.IsSelected);
    }

    /// <summary>
    /// <see cref="RuleResultViewModel.SetScope"/> で件数と明細が範囲内だけになる。
    ///
    /// 上段の「検出 / 修正可 / 保留」列はここを見ている。範囲外を混ぜると、
    /// 行に出ている件数と下段に並ぶ明細の数が食い違う。
    /// </summary>
    [Fact]
    public void SetScope_件数と明細が範囲内だけになる()
    {
        TagChange bach = CreateChange(@"バッハ\01.m4a", Severity.Error, ["Classic"]);
        TagChange bruckner = CreateChange(@"ブルックナー\01.m4a", Severity.Error, ["Classic"]);
        RuleResultViewModel rule = new(CreateResult(Severity.Error, bach, bruckner));

        Assert.Equal(2, rule.Count);
        Assert.Equal(2, rule.FixableCount);

        rule.SetScope(change => change.FolderPath == "バッハ");

        Assert.Equal(1, rule.Count);
        Assert.Equal(1, rule.FixableCount);
        Assert.Equal(bach, Assert.Single(rule.ScopedChanges).Change);

        // 母集合は残っている。範囲を外せば元に戻る。
        Assert.Equal(2, rule.Changes.Count);

        rule.SetScope(null);

        Assert.Equal(2, rule.Count);
    }

    /// <summary>
    /// 範囲内だけを一括操作の対象にし、**範囲外のチェック状態は動かさない**。
    ///
    /// ここが崩れると、1 フォルダを見ているつもりの操作がライブラリ全体のチェックを変える。
    /// </summary>
    [Fact]
    public void SetScope_一括操作は範囲外を動かさない()
    {
        TagChange bach = CreateChange(@"バッハ\01.m4a", Severity.Error, ["Classic"]);
        TagChange bruckner = CreateChange(@"ブルックナー\01.m4a", Severity.Error, ["Classic"]);
        RuleResultViewModel rule = new(CreateResult(Severity.Error, bach, bruckner));

        rule.SetScope(change => change.FolderPath == "バッハ");
        rule.UpdateChangeSelection(_ => false);

        Assert.False(bach.IsSelected);
        Assert.True(bruckner.IsSelected);

        // ヘッダーも範囲内の実態から決まる（範囲内は全解除なので false）。
        Assert.False(rule.IsSelected);
    }

    /// <summary>
    /// 範囲を差し替えた時点で、ヘッダーのチェックが範囲内の実態から決め直される。
    /// </summary>
    [Fact]
    public void SetScope_ヘッダーが範囲内の実態から決まる()
    {
        TagChange bach = CreateChange(@"バッハ\01.m4a", Severity.Error, ["Classic"]);
        TagChange bruckner = CreateChange(@"ブルックナー\01.m4a", Severity.Error, ["Classic"]);
        RuleResultViewModel rule = new(CreateResult(Severity.Error, bach, bruckner));

        // 範囲外だけ外す。全体で見れば「混在」なのでヘッダーは false。
        rule.Changes[1].IsSelected = false;
        Assert.False(rule.IsSelected);

        // バッハ配下だけを見ると全件チェック済みなのでヘッダーは true に戻る。
        rule.SetScope(change => change.FolderPath == "バッハ");

        Assert.True(rule.IsSelected);
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
