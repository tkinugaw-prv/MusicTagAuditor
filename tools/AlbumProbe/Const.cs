namespace AlbumProbe;

/// <summary>
/// 測定で使う定数。docs/llm_guideline.md の規約により、定数は FULL_CAPITAL で定義する。
/// </summary>
public static class Const
{
    /// <summary>ライブラリのパスを省略したときの既定値。</summary>
    public const string DEFAULT_LIBRARY_ROOT = @"D:\Music Library for AIMP\Classic";

    /// <summary>正規化辞書を置くフォルダ名（%APPDATA% 配下）。本体アプリと同じ場所を読む。</summary>
    public const string DICTIONARY_DIRECTORY_NAME = "MusicTagAuditor";

    /// <summary>レポートの既定のファイル名。</summary>
    public const string REPORT_FILE_NAME = "album-probe-report.md";

    /// <summary>
    /// 複合キーを 1 本の文字列にまとめるときの区切り。
    /// タグの値に現れない制御文字を選ぶ。区切りが値に含まれると別のキーが同一視される。
    /// </summary>
    public const string KEY_SEPARATOR = "\u0001";

    /// <summary>
    /// 演奏団体を実体 ID で表すときの接頭辞。
    /// 団体は時代分割で値が変わるため、名前ではなく実体 ID で同一性を見る
    /// （docs/TAGGING_POLICY.md 5.3.1）。生の値と見分けが付くようにする。
    /// </summary>
    public const string ENTITY_ID_PREFIX = "#";

    /// <summary>値が無いことを表に出すときの表示。</summary>
    public const string NO_VALUE = "(なし)";

    /// <summary>ライブラリルート直下を表す表示（docs/SPEC.md 5.2）。</summary>
    public const string ROOT_FOLDER_LABEL = "(root)";
}
