namespace TagIoProbe;

/// <summary>
/// TagIoProbe 全体で使う定数。
/// docs/llm_guideline.md の規約により、定数は FULL_CAPITAL で定義する。
/// </summary>
internal static class Const
{
    /// <summary>検体を展開する作業フォルダ（.gitignore 済み）。</summary>
    public const string WORK_DIR_NAME = "work";

    /// <summary>検証結果レポートのファイル名。</summary>
    public const string REPORT_FILE_NAME = "report.md";

    /// <summary>
    /// M4A の指揮者 atom。AIMP はこれを用いる（docs/TAGGING_POLICY.md 4.3 で検証済み）。
    /// 先頭バイトは著作権記号 0xA9。
    /// </summary>
    public static readonly byte[] ATOM_CONDUCTOR = [0xA9, (byte)'c', (byte)'o', (byte)'n'];

    /// <summary>MP4 のフリーフォーム atom の型名。</summary>
    public const string ATOM_FREEFORM = "----";

    /// <summary>MP4 のカバーアート atom。</summary>
    public const string ATOM_COVER_ART = "covr";

    /// <summary>V1 の書き込みに使う指揮者名。既存値と必ず異なる値を選ぶ。</summary>
    public const string PROBE_CONDUCTOR = "Yevgeny Mravinsky";

    /// <summary>V7（セミコロン分割）の検証に使う値。</summary>
    public const string PROBE_SEMICOLON_VALUE = "Peter Pears(T); Hermann Prey(BR)";

    /// <summary>検証対象の拡張子。docs/SPEC.md 11章の対象拡張子に対応する。</summary>
    public static readonly string[] TARGET_EXTENSIONS = [".m4a", ".flac", ".mp3", ".aif", ".aiff"];
}
