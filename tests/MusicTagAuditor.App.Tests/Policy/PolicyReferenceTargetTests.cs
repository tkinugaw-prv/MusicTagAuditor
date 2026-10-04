using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using MusicTagAuditor.App.Policy;

namespace MusicTagAuditor.App.Tests.Policy;

/// <summary>
/// 画面の文言に書いた原則への参照が、実在の箇所を指していることのテスト（docs/SPEC.md 5.5.1）。
///
/// **参照の番号は文言にしか書いていない。** 原則の節が振り直されたり、文言の番号を書き違えたりすると、
/// リンクは黙って開けなくなる。ソースの文字列リテラルを全部読み、すべてが開けることを確かめる。
/// </summary>
public sealed partial class PolicyReferenceTargetTests
{
    /// <summary>
    /// ソース中のすべての参照が、原則の見出し（と規則）に行き着くことを確認する。
    /// </summary>
    [Fact]
    public void ソースに書いた参照はすべて原則に実在する()
    {
        DispatcherTestRunner.Run(() =>
        {
            PolicyDocument document = PolicyDocument.LoadEmbedded();
            PolicyRendering rendering = new PolicyFlowDocumentBuilder(null).Build(document);
            List<string> broken = [];
            int checkedCount = 0;

            foreach ((string location, PolicyReference reference) in EnumerateSourceReferences())
            {
                checkedCount++;

                if (!Resolves(document, rendering, reference))
                {
                    broken.Add($"{location}: {reference}");
                }
            }

            Assert.True(checkedCount > 0, "参照が 1 件も見つからない。ソースの探し方が壊れている");
            Assert.True(broken.Count == 0, "開けない参照:\n" + string.Join("\n", broken));

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 規則の項目を番号で引けることを確認する。規則が小見出しの下にある節（3.1）も含む。
    /// </summary>
    /// <param name="section">節番号。</param>
    /// <param name="rule">規則の番号。</param>
    /// <param name="expectedStart">項目の書き出し（リストの番号の後ろ）。</param>
    [Theory]
    [InlineData("3.5", 1, "4 要素すべてを常に付ける。")]
    [InlineData("3.5", 8, "作品名の言語は")]
    [InlineData("3.1", 3, "収録時点での名称を採用する")]
    [InlineData("7", 4, "確信が持てない項目は書き換えず")]
    public void 規則の項目を番号で引ける(string section, int rule, string expectedStart)
    {
        DispatcherTestRunner.Run(() =>
        {
            PolicyDocument document = PolicyDocument.LoadEmbedded();
            PolicyRendering rendering = new PolicyFlowDocumentBuilder(null).Build(document);
            PolicyHeading heading = document.FindSection(section)!;

            ListItem? item = rendering.FindRule(document.Headings, heading, rule);

            Assert.NotNull(item);
            // 取り出した文字列はリストの番号から始まる。番号まで比べて、規則番号と項目がずれていないことも確かめる。
            Assert.StartsWith(
                $"{rule}.	{expectedStart}",
                new TextRange(item.ContentStart, item.ContentEnd).Text,
                StringComparison.Ordinal);

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 範囲外の規則番号は見つからないことを確認する。別の規則を開くより、節の先頭で止まるほうがよい。
    /// </summary>
    [Fact]
    public void 範囲外の規則は見つからない()
    {
        DispatcherTestRunner.Run(() =>
        {
            PolicyDocument document = PolicyDocument.LoadEmbedded();
            PolicyRendering rendering = new PolicyFlowDocumentBuilder(null).Build(document);

            Assert.Null(rendering.FindRule(document.Headings, document.FindSection("3.5")!, 99));

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 参照が見出し（規則があれば項目も）に行き着くか。
    /// </summary>
    private static bool Resolves(PolicyDocument document, PolicyRendering rendering, PolicyReference reference)
    {
        if (reference.Supplement is string supplement)
        {
            return document.Headings.Any(heading => heading.Text.StartsWith(supplement, StringComparison.Ordinal));
        }

        PolicyHeading? heading = document.FindSection(reference.Section);

        if (heading is null)
        {
            return false;
        }

        return reference.Rule is not int rule || rendering.FindRule(document.Headings, heading, rule) is not null;
    }

    /// <summary>
    /// src 配下の .cs / .xaml の文字列リテラル（XAML は属性値）から参照を拾う。
    ///
    /// コメントは対象にしない。開発者向けの注記は画面に出ず、リンクにもならないため。
    /// </summary>
    private static IEnumerable<(string Location, PolicyReference Reference)> EnumerateSourceReferences()
    {
        string sourceRoot = FindRepositoryDirectory("src");

        IEnumerable<string> files = Directory
            .EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (string file in files)
        {
            string[] lines = File.ReadAllLines(file);

            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].TrimStart();

                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("<!--", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match literal in StringLiteralRegex().Matches(line))
                {
                    foreach (PolicyReference reference in PolicyReferenceParser.FindAll(literal.Value))
                    {
                        yield return ($"{Path.GetRelativePath(sourceRoot, file)}:{index + 1}", reference);
                    }
                }
            }
        }
    }

    /// <summary>
    /// リポジトリ内のフォルダを、テストの実行ディレクトリから上へ辿って探す。
    /// </summary>
    private static string FindRepositoryDirectory(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, name);

            if (Directory.Exists(candidate) && File.Exists(Path.Combine(directory.FullName, "MusicTagAuditor.slnx")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"{name} が見つかりません。");
    }

    /// <summary>1 行に収まる文字列リテラル（エスケープ込み）。</summary>
    [GeneratedRegex(@"""(?:[^""\\]|\\.)*""")]
    private static partial Regex StringLiteralRegex();
}
