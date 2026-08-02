using System.IO;

namespace MusicTagger.App;

/// <summary>
/// アプリケーション全体で使う定数。
/// docs/llm_guideline.md の規約により、定数は FULL_CAPITAL で定義する。
/// </summary>
public static class AppConst
{
    /// <summary>設定・辞書・ログを置くフォルダ名。</summary>
    public const string APP_FOLDER_NAME = "musicTagger";

    /// <summary>ログファイル名のテンプレート。日付ごとにローテーションする。</summary>
    public const string LOG_FILE_NAME = "musicTagger-.log";

    /// <summary>ログの保存世代数。</summary>
    public const int LOG_RETAINED_FILE_COUNT = 14;

    /// <summary>
    /// 設定・辞書の保存先（<c>%APPDATA%\musicTagger</c>）。docs/SPEC.md 7.1。
    /// </summary>
    /// <returns>フォルダの絶対パス。</returns>
    public static string GetAppDataDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            APP_FOLDER_NAME);
    }

    /// <summary>
    /// ログの保存先（<c>%LOCALAPPDATA%\musicTagger\logs</c>）。
    /// </summary>
    /// <returns>フォルダの絶対パス。</returns>
    public static string GetLogDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            APP_FOLDER_NAME,
            "logs");
    }
}
