using System.Globalization;
using System.Text;
using MusicTagger.Core.Models;

namespace MusicTagger.Core.Export;

/// <summary>
/// 検査結果の差分を CSV に書き出す（docs/SPEC.md 5.1 の CSV 出力）。
///
/// 表計算ソフトで開いて確認・共有するための出力。**根拠列を必ず含める。**
/// 根拠が読めない差分は承認できないという要件は、画面でも CSV でも変わらない。
/// </summary>
public static class ChangeCsvExporter
{
    /// <summary>列見出し。</summary>
    private static readonly string[] HEADERS =
    [
        "ルールID",
        "重大度",
        "区分",
        "適用予定",
        "パス",
        "フォルダ",
        "ファイル名",
        "タグ",
        "変更前",
        "変更後",
        "根拠",
    ];

    /// <summary>重大度の表示名。</summary>
    private static readonly Dictionary<Severity, string> SEVERITY_LABELS = new()
    {
        [Severity.Error] = "エラー",
        [Severity.Warning] = "警告",
        [Severity.Info] = "要確認",
        [Severity.Manual] = "手編集",
    };

    /// <summary>
    /// 差分を CSV 文字列にする。
    /// </summary>
    /// <param name="changes">書き出す差分。</param>
    /// <returns>CSV の中身。</returns>
    public static string Build(IEnumerable<TagChange> changes)
    {
        StringBuilder builder = new();

        builder.AppendLine(string.Join(',', HEADERS.Select(Escape)));

        foreach (TagChange change in changes)
        {
            string[] cells =
            [
                change.RuleId,
                SEVERITY_LABELS[change.Severity],
                change.Classification,
                change.IsSelected ? "○" : string.Empty,
                change.RelativePath,
                Path.GetDirectoryName(change.RelativePath) ?? string.Empty,
                Path.GetFileName(change.RelativePath),
                change.Field.ToString(),
                change.BeforeText,
                change.AfterText,
                change.Rationale,
            ];

            builder.AppendLine(string.Join(',', cells.Select(Escape)));
        }

        return builder.ToString();
    }

    /// <summary>
    /// 差分を CSV ファイルに書き出す。
    /// </summary>
    /// <param name="path">書き出し先。</param>
    /// <param name="changes">書き出す差分。</param>
    public static void WriteFile(string path, IEnumerable<TagChange> changes)
    {
        // BOM 付き UTF-8 で書く。Excel は BOM が無いと日本語を Shift-JIS と誤認する。
        File.WriteAllText(path, Build(changes), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>
    /// ルール別の集計を CSV 文字列にする。全体像を先に見せるための表。
    /// </summary>
    /// <param name="changes">対象の差分。</param>
    /// <returns>CSV の中身。</returns>
    public static string BuildSummary(IEnumerable<TagChange> changes)
    {
        TagChange[] all = [.. changes];

        StringBuilder builder = new();
        builder.AppendLine(string.Join(',', new[] { "ルールID", "検出", "適用予定", "保留" }.Select(Escape)));

        foreach (var group in all.GroupBy(change => change.RuleId).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            string[] cells =
            [
                group.Key,
                group.Count().ToString(CultureInfo.InvariantCulture),
                group.Count(change => change.IsSelected).ToString(CultureInfo.InvariantCulture),
                group.Count(change => change.HoldReason != HoldReason.None).ToString(CultureInfo.InvariantCulture),
            ];

            builder.AppendLine(string.Join(',', cells.Select(Escape)));
        }

        return builder.ToString();
    }

    /// <summary>
    /// CSV のセルとしてエスケープする（RFC 4180）。
    /// パスやアルバム名に読点が含まれるため、囲みは省略しない。
    /// </summary>
    private static string Escape(string value)
    {
        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }
}
