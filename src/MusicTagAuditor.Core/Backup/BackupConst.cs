using System.Globalization;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Backup;

/// <summary>
/// バックアップと復元で使う定数。
/// docs/llm_guideline.md の規約により、定数は FULL_CAPITAL で定義する。
/// </summary>
public static class BackupConst
{
    /// <summary>スナップショット JSON のスキーマ版。</summary>
    public const int SCHEMA_VERSION = 2;

    /// <summary><c>comment</c> を記録するようになった版。</summary>
    public const int SCHEMA_VERSION_WITH_COMMENT = 2;

    /// <summary>
    /// フィールドごとに、それを記録するようになったスキーマ版。ここに無いフィールドは版 1 から記録している。
    ///
    /// 版を上げてフィールドを足すときは <see cref="SCHEMA_VERSION"/> を上げ、この表に 1 行足す。
    /// 復元側のロジックには触らなくてよい。
    /// </summary>
    private static readonly IReadOnlyDictionary<TagField, int> FIRST_VERSION_BY_FIELD =
        new Dictionary<TagField, int>
        {
            [TagField.Comment] = SCHEMA_VERSION_WITH_COMMENT,
        };

    /// <summary>
    /// そのスキーマ版のスナップショットがフィールドを記録しているかを返す。
    ///
    /// 記録していない版では、スナップショットに値が無いことを「空だった」と読んではならない。
    /// 両者を区別できないまま復元すると、現在入っている値を消してしまう。
    /// </summary>
    /// <param name="schemaVersion">スナップショットのスキーマ版。</param>
    /// <param name="field">対象フィールド。</param>
    /// <returns>記録している版なら true。</returns>
    public static bool IsFieldRecorded(int schemaVersion, TagField field)
    {
        return !FIRST_VERSION_BY_FIELD.TryGetValue(field, out int firstVersion)
            || schemaVersion >= firstVersion;
    }

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
