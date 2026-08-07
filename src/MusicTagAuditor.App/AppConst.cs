using System.IO;

namespace MusicTagAuditor.App;

/// <summary>
/// アプリケーション全体で使う定数。
/// docs/llm_guideline.md の規約により、定数は FULL_CAPITAL で定義する。
/// </summary>
public static class AppConst
{
    /// <summary>設定・辞書・ログを置くフォルダ名。</summary>
    public const string APP_FOLDER_NAME = "MusicTagAuditor";

    /// <summary>ログファイル名のテンプレート。日付ごとにローテーションする。</summary>
    public const string LOG_FILE_NAME = "MusicTagAuditor-.log";

    /// <summary>ログの保存世代数。</summary>
    public const int LOG_RETAINED_FILE_COUNT = 14;

    /// <summary>検査結果 CSV の既定ファイル名の接頭辞。後ろに日時が付く。</summary>
    public const string CHANGE_CSV_FILE_NAME_PREFIX = "MusicTagAuditor-changes-";

    /// <summary>同梱の既定辞書のファイル名。</summary>
    public const string BUNDLED_DICTIONARY_FILE_NAME = "default-dictionary.json";

    /// <summary>リポジトリ内での既定辞書の位置。</summary>
    private static readonly string[] BUNDLED_DICTIONARY_SEGMENTS =
        ["src", "MusicTagAuditor.Core", "Dictionary", BUNDLED_DICTIONARY_FILE_NAME];

    /// <summary>開発中に上へ辿る最大の段数。</summary>
    private const int BUNDLED_DICTIONARY_SEARCH_DEPTH = 8;

    /// <summary>
    /// 設定・辞書の保存先（<c>%APPDATA%\MusicTagAuditor</c>）。docs/SPEC.md 7.1。
    /// </summary>
    /// <returns>フォルダの絶対パス。</returns>
    public static string GetAppDataDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            APP_FOLDER_NAME);
    }

    /// <summary>
    /// ログの保存先（<c>%LOCALAPPDATA%\MusicTagAuditor\logs</c>）。
    /// </summary>
    /// <returns>フォルダの絶対パス。</returns>
    public static string GetLogDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            APP_FOLDER_NAME,
            "logs");
    }

    /// <summary>
    /// リポジトリ内の既定辞書を探す。
    ///
    /// 既定辞書は埋め込みリソースなので、実行中のアセンブリから書き戻すことはできない。
    /// 開発中に「育てた辞書を同梱側へ戻す」ための書き出し先の初期値として、
    /// 実行ディレクトリから上へ辿ってソースを探す。配布物では見つからず null を返す。
    /// </summary>
    /// <returns>既定辞書の絶対パス。見つからなければ null。</returns>
    public static string? FindBundledDictionaryPath()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        for (int depth = 0; depth < BUNDLED_DICTIONARY_SEARCH_DEPTH && directory is not null; depth++)
        {
            string candidate = Path.Combine([directory.FullName, .. BUNDLED_DICTIONARY_SEGMENTS]);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
