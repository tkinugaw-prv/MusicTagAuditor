using System.IO;
using MusicTagAuditor.App.Policy;

namespace MusicTagAuditor.App.Tests.Policy;

/// <summary>
/// 画面で読むタグ付け原則の読み込みと目次のテスト（docs/SPEC.md 5.5）。
///
/// 埋め込んだ原本そのものを対象にする。原則が改訂されて章立てが変わったとき、
/// 画面の前提（6 章を折りたたむ等）が黙って外れるのをここで検出する。
/// </summary>
public sealed class PolicyDocumentTests
{
    /// <summary>
    /// 埋め込んだ本文がリポジトリの原本と一致することを確認する。写しを持たない設計の確認。
    /// </summary>
    [Fact]
    public void 埋め込んだ原則はリポジトリの原本と一致する()
    {
        string original = File.ReadAllText(FindRepositoryFile("docs", "TAGGING_POLICY.md"));

        Assert.Equal(original, PolicyDocument.ReadEmbeddedText());
    }

    /// <summary>
    /// 折りたたむ章が「未確定・未完了の事項」を指していることを確認する。
    /// 章番号が振り直されると、規則の章を折りたたんでしまう。
    /// </summary>
    [Fact]
    public void 折りたたむ章は未確定事項の章である()
    {
        PolicyDocument document = PolicyDocument.LoadEmbedded();

        foreach (string number in PolicyDocument.COLLAPSED_CHAPTER_NUMBERS)
        {
            PolicyHeading? chapter = document.FindSection(number);

            Assert.NotNull(chapter);
            Assert.Equal(2, chapter.Level);
            Assert.Contains("未確定", chapter.Text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 節番号の取り出しを確認する。
    /// </summary>
    /// <param name="heading">見出し。</param>
    /// <param name="expected">期待する節番号。</param>
    [Theory]
    [InlineData("1. この文書の位置づけ", "1")]
    [InlineData("3.5 アルバム名", "3.5")]
    [InlineData("3.1.2 団体名", "3.1.2")]
    [InlineData("3.5 補足3：規則6（対象外）の実測", "3.5")]
    [InlineData("3.1 補足：なぜラテン文字で統一するか", "3.1")]
    [InlineData("クラシック音楽ライブラリ タグ付け原則", null)]
    [InlineData("2026年の記録", null)]
    public void 見出しから節番号を取り出す(string heading, string? expected)
    {
        Assert.Equal(expected, PolicyDocument.ExtractNumber(heading));
    }

    /// <summary>
    /// 同じ番号の補足より、本体の節が先に当たることを確認する。
    /// </summary>
    [Fact]
    public void 節番号で引くと補足ではなく本体の節が当たる()
    {
        PolicyDocument document = PolicyDocument.LoadEmbedded();

        PolicyHeading? heading = document.FindSection("3.5");

        Assert.NotNull(heading);
        Assert.Equal(3, heading.Level);
        Assert.StartsWith("3.5 アルバム名", heading.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 番号を持たない深い見出しも、直前の章に属することを確認する。
    /// </summary>
    [Fact]
    public void 見出しは直前の章に属する()
    {
        PolicyDocument document = new("""
            # 表題

            ## 6. 未確定

            ### 6.1 残作業

            ##### 補足（番号なし）

            ## 7. 作業手順
            """);

        Assert.Equal(
            [null, "6", "6", "6", "7"],
            document.Headings.Select(heading => heading.ChapterNumber));
        Assert.Equal(
            [false, true, true, true, false],
            document.Headings.Select(heading => heading.IsInCollapsedChapter));
    }

    /// <summary>
    /// 目次は表題を除き、章から補足までを載せることを確認する。
    /// </summary>
    [Fact]
    public void 目次は表題を除いて章から補足までを載せる()
    {
        PolicyDocument document = new("""
            # 表題

            ## 1. 章

            ### 1.1 節

            ###### 深すぎる見出し

            ##### 1.1 補足
            """);

        IReadOnlyList<PolicyOutlineItem> outline = PolicyOutlineItem.CreateOutline(document);

        Assert.Equal(["1. 章", "1.1 節", "1.1 補足"], outline.Select(item => item.Text));
        Assert.True(outline[0].IsChapter);
    }

    /// <summary>
    /// リポジトリ内のファイルを、テストの実行ディレクトリから上へ辿って探す。
    /// </summary>
    private static string FindRepositoryFile(params string[] segments)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine([directory.FullName, .. segments]);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{Path.Combine(segments)} が見つかりません。");
    }
}
