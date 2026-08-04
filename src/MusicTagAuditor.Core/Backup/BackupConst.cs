using System.Globalization;

namespace MusicTagAuditor.Core.Backup;

/// <summary>
/// バックアップと復元で使う定数。
/// docs/llm_guideline.md の規約により、定数は FULL_CAPITAL で定義する。
/// </summary>
public static class BackupConst
{
    /// <summary>スナップショット JSON のスキーマ版。</summary>
    public const int SCHEMA_VERSION = 1;

    /// <summary>バックアップフォルダ名の接頭辞。スキャンの除外規則と一致させること。</summary>
    public const string BACKUP_DIRECTORY_PREFIX = "backup_";

    /// <summary>バックアップフォルダ名の日時書式。</summary>
    public const string BACKUP_DIRECTORY_TIMESTAMP_FORMAT = "yyyyMMddHHmmss";

    /// <summary>同名フォルダを避けるための連番の区切り。</summary>
    public const string SEQUENCE_SEPARATOR = "_";

    /// <summary>タグのスナップショットファイル名。</summary>
    public const string SNAPSHOT_FILE_NAME = "tags_snapshot.json";

    /// <summary>マニフェストファイル名。</summary>
    public const string MANIFEST_FILE_NAME = "manifest.json";

    /// <summary>アプリ無しで復元するための PowerShell スクリプト名。</summary>
    public const string RESTORE_SCRIPT_FILE_NAME = "restore-tags.ps1";

    /// <summary>
    /// バックアップフォルダ名を組み立てる。
    /// </summary>
    /// <param name="timestamp">取得日時。</param>
    /// <returns>フォルダ名（例: <c>backup_20260803031500</c>）。</returns>
    public static string BuildDirectoryName(DateTimeOffset timestamp)
    {
        return BACKUP_DIRECTORY_PREFIX
            + timestamp.ToString(BACKUP_DIRECTORY_TIMESTAMP_FORMAT, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// フォルダ名に衝突回避の連番を足す。
    /// </summary>
    /// <param name="directoryName">連番なしのフォルダ名。</param>
    /// <param name="sequence">連番。</param>
    /// <returns>連番付きのフォルダ名（例: <c>backup_20260803031500_2</c>）。</returns>
    public static string AppendSequence(string directoryName, int sequence)
    {
        return directoryName
            + SEQUENCE_SEPARATOR
            + sequence.ToString(CultureInfo.InvariantCulture);
    }
}
