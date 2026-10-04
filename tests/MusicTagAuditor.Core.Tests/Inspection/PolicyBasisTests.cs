using MusicTagAuditor.Core.Inspection;

namespace MusicTagAuditor.Core.Tests.Inspection;

/// <summary>
/// 検査ルールの根拠（docs/SPEC.md 6.1 の「根拠」列）のテスト。
///
/// 根拠が原則に実在するかは原則の本文を読む App 側で確かめる（PolicyReferenceTargetTests）。
/// ここでは、どのルールも根拠を持つことだけを押さえる。
/// </summary>
public sealed class PolicyBasisTests
{
    /// <summary>
    /// 既定のルールがすべて根拠を 1 つ以上持つことを確認する。
    /// </summary>
    [Fact]
    public void すべてのルールが根拠を持つ()
    {
        string[] missing =
        [
            .. InspectionEngine.CreateDefaultRules()
                .Where(rule => rule.PolicyBases.Count == 0)
                .Select(rule => rule.Id),
        ];

        Assert.Empty(missing);
    }

    /// <summary>
    /// 原則での書き方にできることを確認する。
    /// </summary>
    [Fact]
    public void 原則での書き方にする()
    {
        Assert.Equal("5.2", new PolicyBasis("5.2").ToString());
        Assert.Equal("3.5 規則6", new PolicyBasis("3.5", 6).ToString());
    }
}
