using System.Windows.Controls;
using System.Windows.Documents;
using MusicTagAuditor.App.Controls;

namespace MusicTagAuditor.App.Tests.Controls;

/// <summary>
/// ルール ID の後ろに根拠の節をリンクで添える表示のテスト（docs/SPEC.md 5.5.2）。
/// </summary>
public sealed class PolicyTextRuleIdsTests
{
    /// <summary>
    /// 同じ節の規則は節番号を 1 度だけ書き、規則ごとにリンクを分けることを確認する。
    /// </summary>
    [Fact]
    public void 同じ節の規則は節番号をまとめてリンクを分ける()
    {
        DispatcherTestRunner.Run(() =>
        {
            TextBlock block = Render("R-501");

            Assert.Equal("R-501  3.5 規則5・規則6", GetText(block));
            Assert.Equal(["3.5 規則5", "規則6"], GetLinkTexts(block));

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 根拠が複数の節にわたるルールは、節ごとにリンクを分けることを確認する。
    /// </summary>
    [Fact]
    public void 別の節の根拠は節ごとにリンクを分ける()
    {
        DispatcherTestRunner.Run(() =>
        {
            TextBlock block = Render("R-202");

            Assert.Equal("R-202  5.2 / 5.3", GetText(block));
            Assert.Equal(["5.2", "5.3"], GetLinkTexts(block));

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 複数のルールを並べられ、表に無い ID は文字だけで出ることを確認する。
    /// </summary>
    [Fact]
    public void 複数のルールを並べ表に無いIDは文字だけで出す()
    {
        DispatcherTestRunner.Run(() =>
        {
            TextBlock block = Render(new[] { "R-201", "MANUAL" });

            Assert.Equal("R-201  5.1   MANUAL", GetText(block));
            Assert.Equal(["5.1"], GetLinkTexts(block));

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// ルール ID を設定した TextBlock を作る。
    /// </summary>
    private static TextBlock Render(object ruleIds)
    {
        TextBlock block = new();
        PolicyText.SetRuleIds(block, ruleIds);

        return block;
    }

    /// <summary>
    /// 表示される文字列。
    ///
    /// TextRange で読むと、TextBlock の末尾にあるリンクの中身が落ちる。要素の Run を直接読んでつなぐ。
    /// </summary>
    private static string GetText(TextBlock block)
    {
        return string.Concat(block.Inlines.Select(ReadInline));
    }

    /// <summary>
    /// リンクの文字列。
    /// </summary>
    private static string[] GetLinkTexts(TextBlock block)
    {
        return [.. block.Inlines.OfType<Hyperlink>().Select(ReadInline)];
    }

    /// <summary>
    /// 行内要素の文字列を読む。
    /// </summary>
    private static string ReadInline(Inline inline)
    {
        return inline switch
        {
            Run run => run.Text,
            Span span => string.Concat(span.Inlines.Select(ReadInline)),
            _ => string.Empty,
        };
    }
}
