using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Tests.Inspection;

/// <summary>
/// <see cref="InspectionResult.RemoveChanges"/> のテスト。
///
/// 適用に成功した項目だけを検査結果から取り除く（検査結果を保持してほしいという要望への対応）ための
/// 中核ロジックを確認する。
/// </summary>
public sealed class InspectionResultTests
{
    /// <summary>
    /// 指定したキーに一致する項目だけが除去され、一致しない項目は残ることを確認する。
    /// </summary>
    [Fact]
    public void RemovesOnlyMatchingChanges()
    {
        TagChange keep = Change("01.m4a", TagField.Composer, "R-201");
        TagChange remove = Change("01.m4a", TagField.Genre, "R-102");

        InspectionResult result = new([Rule("R-201", keep, remove)], TimeSpan.Zero);

        InspectionResult trimmed = result.RemoveChanges(
            new HashSet<TagChangeKey> { TagChangeKey.Of("01.m4a", TagField.Genre) });

        TagChange onlyChange = Assert.Single(trimmed.AllChanges);
        Assert.Equal(TagField.Composer, onlyChange.Field);
    }

    /// <summary>
    /// 除去後に 0 件になったルールは <see cref="InspectionResult.Results"/> から落ちることを確認する。
    /// 検査直後に 0 件のルールを画面に出さない基準と揃える。
    /// </summary>
    [Fact]
    public void DropsRuleWhenAllChangesRemoved()
    {
        TagChange only = Change("01.m4a", TagField.Genre, "R-102");
        InspectionResult result = new([Rule("R-102", only)], TimeSpan.Zero);

        InspectionResult trimmed = result.RemoveChanges(
            new HashSet<TagChangeKey> { TagChangeKey.Of("01.m4a", TagField.Genre) });

        Assert.Empty(trimmed.Results);
    }

    /// <summary>
    /// 何も除去されなかったルールは、同一インスタンスのまま返ることを確認する。
    /// 呼び出し側（ビューモデル）が無関係な行まで作り直さずに済むようにするため。
    /// </summary>
    [Fact]
    public void ReusesUntouchedRuleInstance()
    {
        RuleResult untouched = Rule("R-201", Change("01.m4a", TagField.Composer, "R-201"));
        RuleResult touched = Rule("R-102", Change("02.m4a", TagField.Genre, "R-102"));

        InspectionResult result = new([untouched, touched], TimeSpan.Zero);

        InspectionResult trimmed = result.RemoveChanges(
            new HashSet<TagChangeKey> { TagChangeKey.Of("02.m4a", TagField.Genre) });

        RuleResult remaining = Assert.Single(trimmed.Results);
        Assert.Same(untouched, remaining);
    }

    /// <summary>
    /// 一部だけ除去されたルールは、新しい <see cref="RuleResult"/> インスタンスになることを確認する。
    /// </summary>
    [Fact]
    public void CreatesNewInstanceForPartiallyTrimmedRule()
    {
        TagChange keep = Change("01.m4a", TagField.Composer, "R-201");
        TagChange remove = Change("01.m4a", TagField.Genre, "R-201");
        RuleResult original = Rule("R-201", keep, remove);

        InspectionResult result = new([original], TimeSpan.Zero);

        InspectionResult trimmed = result.RemoveChanges(
            new HashSet<TagChangeKey> { TagChangeKey.Of("01.m4a", TagField.Genre) });

        RuleResult remaining = Assert.Single(trimmed.Results);
        Assert.NotSame(original, remaining);
        Assert.Same(keep, Assert.Single(remaining.Changes));
    }

    /// <summary>
    /// 空集合を渡すと自分自身をそのまま返すことを確認する。
    /// </summary>
    [Fact]
    public void ReturnsSameInstanceForEmptyKeySet()
    {
        InspectionResult result = new(
            [Rule("R-102", Change("01.m4a", TagField.Genre, "R-102"))],
            TimeSpan.Zero);

        InspectionResult trimmed = result.RemoveChanges(new HashSet<TagChangeKey>());

        Assert.Same(result, trimmed);
    }

    /// <summary>
    /// テスト用の修正案を作る。
    /// </summary>
    private static TagChange Change(string relativePath, TagField field, string ruleId)
    {
        return new TagChange(relativePath, field, [], ["変更後"], ruleId, "テスト用", Severity.Error);
    }

    /// <summary>
    /// テスト用のルール結果を作る。
    /// </summary>
    private static RuleResult Rule(string ruleId, params TagChange[] changes)
    {
        return new RuleResult(ruleId, Severity.Error, "テスト用ルール", changes);
    }
}
