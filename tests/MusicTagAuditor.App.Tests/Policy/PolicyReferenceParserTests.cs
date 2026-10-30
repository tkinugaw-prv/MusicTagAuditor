using MusicTagAuditor.App.Policy;

namespace MusicTagAuditor.App.Tests.Policy;

/// <summary>
/// 文言から原則への参照を読むテスト（docs/SPEC.md 5.5.1）。
///
/// 読む書式を絞っているのは、SPEC の節番号や日付・件数をリンクにしないため。
/// 読み落とし（リンクにならない）より、読み違い（別の節が開く）のほうが害が大きい。
/// </summary>
public sealed class PolicyReferenceParserTests
{
    /// <summary>
    /// 接頭辞付きの参照を読むことを確認する。
    /// </summary>
    [Fact]
    public void 接頭辞付きの節番号を読む()
    {
        PolicyReference reference = Assert.Single(PolicyReferenceParser.FindAll("ラテン文字で表記します（TAGGING_POLICY 3.1）。"));

        Assert.Equal(new PolicyReference("3.1"), reference);
    }

    /// <summary>
    /// 規則の参照を、節と規則番号の組で読むことを確認する。
    /// </summary>
    [Fact]
    public void 節と規則の組を読む()
    {
        Assert.Equal(
            [new PolicyReference("3.5", 8)],
            PolicyReferenceParser.FindAll("（TAGGING_POLICY 3.5 規則8）"));
        Assert.Equal(
            [new PolicyReference("3.5", 5)],
            PolicyReferenceParser.FindAll("作曲家を指定する（3.5 規則5）"));
    }

    /// <summary>
    /// 「・規則N」で続く規則を、それぞれ別のリンクにすることを確認する。
    /// </summary>
    [Fact]
    public void 続く規則はそれぞれ別のリンクにする()
    {
        IReadOnlyList<PolicyTextSegment> segments = PolicyReferenceParser.Split("例外（TAGGING_POLICY 3.5 規則2・規則4）。");

        Assert.Equal(
            ["例外（", "TAGGING_POLICY 3.5 規則2", "・", "規則4", "）。"],
            segments.Select(segment => segment.Text));
        Assert.Equal(
            [null, new PolicyReference("3.5", 2), null, new PolicyReference("3.5", 4), null],
            segments.Select(segment => segment.Reference));
    }

    /// <summary>
    /// 補足と「N章 原則M」を読むことを確認する。
    /// </summary>
    [Fact]
    public void 補足と章の原則を読む()
    {
        Assert.Equal(
            [new PolicyReference("3.5", null, "3.5 補足2")],
            PolicyReferenceParser.FindAll("機械が採用してはならない（TAGGING_POLICY 3.5 補足2）"));
        Assert.Equal(
            [new PolicyReference("3.1", null, "3.1 補足")],
            PolicyReferenceParser.FindAll("理由は 3.1 補足 を参照"));
        Assert.Equal(
            [new PolicyReference("7", 4)],
            PolicyReferenceParser.FindAll("推測で埋めない（7章 原則4）"));
    }

    /// <summary>
    /// 原則への参照ではない数字を読まないことを確認する。
    /// </summary>
    /// <param name="text">文言。</param>
    [Theory]
    [InlineData("辞書に作品を足す（SPEC 7.4）")]
    [InlineData("SPEC 7.4 規則3 は存在しない")]
    [InlineData("docs/SPEC.md 7.4 規則1")]
    [InlineData("2026-08-11 時点で 1,041 ファイル")]
    [InlineData("3.5 の書式で組み立てた")]
    [InlineData("対象外（規則6）には当たりません")]
    [InlineData("v1.2.3 規則")]
    public void 原則への参照ではない数字は読まない(string text)
    {
        Assert.Empty(PolicyReferenceParser.FindAll(text));
    }

    /// <summary>
    /// 区切りをつなぐと元の文言に戻ることを確認する。表示から文字が欠けない。
    /// </summary>
    [Fact]
    public void 区切りをつなぐと元の文言に戻る()
    {
        const string TEXT = "主作品が定まるなら作曲家を指定する（3.5 規則5）。定まらないなら対象外（規則6）。7章 原則4";

        Assert.Equal(TEXT, string.Concat(PolicyReferenceParser.Split(TEXT).Select(segment => segment.Text)));
    }

    /// <summary>
    /// 空の文言で区切りが無いことを確認する。
    /// </summary>
    [Fact]
    public void 空の文言は区切りを返さない()
    {
        Assert.Empty(PolicyReferenceParser.Split(null));
        Assert.Empty(PolicyReferenceParser.Split(string.Empty));
    }
}
