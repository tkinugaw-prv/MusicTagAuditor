namespace TagIoProbe;

/// <summary>
/// 検証項目 1 件の結果。docs/SPEC.md 4.1 の V1〜V8 に対応する。
/// </summary>
/// <param name="Id">検証項目 ID（V1〜V8）。</param>
/// <param name="Library">検証したライブラリ名。</param>
/// <param name="Format">対象フォーマット。フォーマット非依存の項目は "-"。</param>
/// <param name="Verdict">判定（OK / NG / N/A / ERROR）。</param>
/// <param name="Detail">判定の根拠。実際に観測した値を必ず書く。</param>
internal sealed record CheckResult(
    string Id,
    string Library,
    string Format,
    string Verdict,
    string Detail);

/// <summary>
/// <see cref="CheckResult.Verdict"/> に入れる判定値。
/// </summary>
internal static class Verdict
{
    /// <summary>要件を満たす。</summary>
    public const string OK = "OK";

    /// <summary>要件を満たさない。</summary>
    public const string NG = "NG";

    /// <summary>対象外（そのフォーマットに該当機能が無い等）。</summary>
    public const string NOT_APPLICABLE = "N/A";

    /// <summary>検証中に例外が発生した。</summary>
    public const string ERROR = "ERROR";
}
