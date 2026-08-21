using System.Text;

namespace MusicTagAuditor.Core.Export;

/// <summary>
/// CSV の書式を揃えるための共通処理。
///
/// 出力するのが検査結果だけだったころは <see cref="ChangeCsvExporter"/> の中に
/// 抱えていたが、ファイル一覧の書き出しが増えて 2 箇所になった。**囲みと BOM の判断は
/// 1 箇所に置く。** 片方だけ直されると、同じ CSV のはずが Excel での読み方が変わる。
/// </summary>
public static class CsvFormat
{
    /// <summary>
    /// CSV のセルとしてエスケープする（RFC 4180）。
    /// パスやアルバム名に読点が含まれるため、囲みは省略しない。
    /// </summary>
    /// <param name="value">セルの値。</param>
    /// <returns>囲みとエスケープを施した文字列。</returns>
    public static string Escape(string value)
    {
        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    /// <summary>
    /// セルを 1 行分の CSV にする。
    /// </summary>
    /// <param name="cells">左から順に並べたセルの値。</param>
    /// <returns>改行を含まない 1 行分の文字列。</returns>
    public static string BuildLine(IEnumerable<string> cells)
    {
        return string.Join(',', cells.Select(Escape));
    }

    /// <summary>
    /// CSV をファイルに書き出す。
    /// </summary>
    /// <param name="path">書き出し先。</param>
    /// <param name="content">CSV の中身。</param>
    public static void WriteFile(string path, string content)
    {
        // BOM 付き UTF-8 で書く。Excel は BOM が無いと日本語を Shift-JIS と誤認する。
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
