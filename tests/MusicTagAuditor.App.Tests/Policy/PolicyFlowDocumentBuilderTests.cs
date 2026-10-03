using System.Windows.Documents;
using MusicTagAuditor.App.Policy;

namespace MusicTagAuditor.App.Tests.Policy;

/// <summary>
/// 原則文書の組み立て・折りたたみ・検索のテスト（docs/SPEC.md 5.5）。
///
/// <see cref="FlowDocument"/> は STA スレッドでしか作れないため、ディスパッチャ上で走らせる。
/// </summary>
public sealed class PolicyFlowDocumentBuilderTests
{
    /// <summary>折りたたみを確かめる最小の原則。</summary>
    private const string FOLDED_POLICY = """
        # 表題

        ## 5. 辞書

        辞書の本文。

        ## 6. 未確定・未完了の事項

        ### 6.1 残作業

        折りたたみ中だけにある語。

        ### 6.2 もう 1 つ

        ## 7. 作業手順

        手順の本文。
        """;

    /// <summary>
    /// 折りたたむ章の本文は、初期状態では文書に入らないことを確認する。見出しは残す。
    /// </summary>
    [Fact]
    public void 折りたたむ章は見出しだけを出し本文は閉じておく()
    {
        DispatcherTestRunner.Run(() =>
        {
            PolicyRendering rendering = Build(FOLDED_POLICY);
            string text = GetText(rendering.Document.Blocks);

            Assert.Contains("6. 未確定・未完了の事項", text, StringComparison.Ordinal);
            Assert.Contains("手順の本文。", text, StringComparison.Ordinal);
            Assert.DoesNotContain("折りたたみ中だけにある語", text, StringComparison.Ordinal);

            PolicyChapterFold fold = Assert.Single(rendering.Folds);
            Assert.False(fold.IsExpanded);
            Assert.Contains("2 節", GetText([fold.TogglePanel]), StringComparison.Ordinal);

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 開くと見出しの直後（7 章より前）に本文が入り、閉じると抜けることを確認する。
    /// </summary>
    [Fact]
    public void 開くと章の位置に本文が入り閉じると抜ける()
    {
        DispatcherTestRunner.Run(() =>
        {
            PolicyRendering rendering = Build(FOLDED_POLICY);
            PolicyChapterFold fold = rendering.Folds[0];

            fold.Expand();
            string expanded = GetText(rendering.Document.Blocks);

            Assert.True(fold.IsExpanded);
            Assert.True(
                expanded.IndexOf("折りたたみ中だけにある語", StringComparison.Ordinal)
                < expanded.IndexOf("7. 作業手順", StringComparison.Ordinal));

            fold.Collapse();

            Assert.False(fold.IsExpanded);
            Assert.DoesNotContain("折りたたみ中だけにある語", GetText(rendering.Document.Blocks), StringComparison.Ordinal);

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 番号付きリストが原文の開始番号を引き継ぐことを確認する。
    /// 規則の一部だけを抜き出して続きから番号を振る書き方があり、1 から振り直すと規則番号がずれる。
    /// </summary>
    [Fact]
    public void 番号付きリストは原文の開始番号を引き継ぐ()
    {
        DispatcherTestRunner.Run(() =>
        {
            PolicyRendering rendering = Build("""
                3. 規則3
                4. 規則4
                """);

            List[] lists = [.. rendering.Document.Blocks.OfType<List>()];

            Assert.Contains(lists, list => list.StartIndex == 3);

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 太字やコードの境目をまたぐ語も見つかることを確認する。Run ごとに探すと見落とす。
    /// </summary>
    [Fact]
    public void 検索は装飾の境目をまたいで一致する()
    {
        DispatcherTestRunner.Run(() =>
        {
            PolicyRendering rendering = Build("`composer` を**先に**確定させる");

            TextRange match = Assert.Single(PolicyTextSearch.FindAll(rendering.Document.Blocks, "composer を先に"));

            Assert.Equal("composer を先に", match.Text);

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 表のセル・リストの中も検索の対象になり、大文字・小文字を区別しないことを確認する。
    /// </summary>
    [Fact]
    public void 検索は表とリストの中も対象にする()
    {
        DispatcherTestRunner.Run(() =>
        {
            PolicyRendering rendering = Build("""
                | 要素 | 内容 |
                |---|---|
                | `{date}` | 録音年 |

                - Date は 4 桁
                """);

            IReadOnlyList<TextRange> matches = PolicyTextSearch.FindAll(rendering.Document.Blocks, "date");

            Assert.Equal(2, matches.Count);

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 折りたたみ中の本文も、本体を指定すれば数えられることを確認する。画面の案内に使う。
    /// </summary>
    [Fact]
    public void 折りたたみ中の本文の一致を数えられる()
    {
        DispatcherTestRunner.Run(() =>
        {
            PolicyRendering rendering = Build(FOLDED_POLICY);

            Assert.Equal(0, PolicyTextSearch.Count(rendering.Document.Blocks, "折りたたみ中だけ"));
            Assert.Equal(1, PolicyTextSearch.Count(rendering.Folds[0].Body.Blocks, "折りたたみ中だけ"));

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 実際の原則文書を組み立てても例外にならず、すべての見出しに段落が対応することを確認する。
    /// </summary>
    [Fact]
    public void 実際の原則文書を組み立てられる()
    {
        DispatcherTestRunner.Run(() =>
        {
            PolicyDocument document = PolicyDocument.LoadEmbedded();
            PolicyRendering rendering = new PolicyFlowDocumentBuilder(null).Build(document);

            Assert.Equal(document.Headings.Count, rendering.HeadingParagraphs.Count);
            Assert.Single(rendering.Folds);

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 既定色で組み立てる。
    /// </summary>
    private static PolicyRendering Build(string markdown)
    {
        return new PolicyFlowDocumentBuilder(null).Build(new PolicyDocument(markdown));
    }

    /// <summary>
    /// ブロック要素の文字列を取り出す。
    /// </summary>
    private static string GetText(IEnumerable<Block> blocks)
    {
        Block[] array = [.. blocks];

        if (array.Length == 0)
        {
            return string.Empty;
        }

        return new TextRange(array[0].ContentStart, array[^1].ContentEnd).Text;
    }
}
