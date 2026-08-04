using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;

namespace MusicTagAuditor.Core.Backup;

/// <summary>
/// 保存済みバックアップ 1 件の所在。
/// </summary>
/// <param name="DirectoryPath">バックアップフォルダの絶対パス。</param>
/// <param name="DirectoryName">フォルダ名。</param>
/// <param name="Manifest">読み込めたマニフェスト。読めない場合は null。</param>
public sealed record BackupEntry(string DirectoryPath, string DirectoryName, BackupManifest? Manifest);

/// <summary>
/// タグのスナップショットを取得・読み込みする。
///
/// **音声ファイル本体は複製しない**（対象ライブラリは 30GB。docs/SPEC.md 8.1）。
/// スナップショットと一緒に復元用 PowerShell を書き出し、アプリが無くても巻き戻せるようにする。
/// </summary>
public sealed class SnapshotService
{
    /// <summary>埋め込まれた復元スクリプトのリソース名の末尾。</summary>
    private const string RESTORE_SCRIPT_RESOURCE_SUFFIX = "restore-tags.ps1";

    /// <summary>
    /// スナップショット出力用のシリアライズ設定。
    /// 日本語をエスケープせずに出す。人と外部スクリプトが直接読むファイルであり、
    /// <c>ブ...</c> の羅列では「アプリ無しでも扱える」という要件を満たせないため。
    /// HTML に埋め込む用途は無いので緩い方のエンコーダで問題ない。
    /// </summary>
    private static readonly BackupJsonContext JSON_CONTEXT = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    /// <summary>
    /// スキャン結果からスナップショットを作り、バックアップフォルダへ書き出す。
    /// </summary>
    /// <param name="scan">スナップショットの元にするスキャン結果。</param>
    /// <param name="reason">取得理由。</param>
    /// <param name="note">補足。適用直前のスナップショットでは適用内容の要約を入れる。</param>
    /// <param name="portableLibraryPath">復元スクリプトが使うタグライブラリ。null なら同梱しない。</param>
    /// <param name="timestamp">フォルダ名に使う日時。省略時は現在時刻。</param>
    /// <returns>作成したバックアップフォルダの絶対パス。</returns>
    public string Create(
        ScanResult scan,
        SnapshotReason reason,
        string? note = null,
        string? portableLibraryPath = null,
        DateTimeOffset? timestamp = null)
    {
        DateTimeOffset createdAt = timestamp ?? DateTimeOffset.Now;

        string directoryPath = Path.Combine(scan.LibraryRoot, BackupConst.BuildDirectoryName(createdAt));
        Directory.CreateDirectory(directoryPath);

        TagSnapshot snapshot = new(
            BackupConst.SCHEMA_VERSION,
            createdAt,
            scan.LibraryRoot,
            scan.Tracks.Count,
            [.. scan.Tracks.Select(ToSnapshotTrack)]);

        BackupManifest manifest = new(
            BackupConst.SCHEMA_VERSION,
            createdAt,
            reason.ToString(),
            scan.LibraryRoot,
            scan.Tracks.Count,
            scan.Failures.Count,
            note);

        WriteJson(Path.Combine(directoryPath, BackupConst.SNAPSHOT_FILE_NAME), snapshot);
        WriteJson(Path.Combine(directoryPath, BackupConst.MANIFEST_FILE_NAME), manifest);
        WriteRestoreScript(directoryPath, portableLibraryPath);

        return directoryPath;
    }

    /// <summary>
    /// ライブラリ直下のバックアップフォルダを新しい順に列挙する。
    /// </summary>
    /// <param name="libraryRoot">ライブラリのルート。</param>
    /// <returns>バックアップの一覧。</returns>
    public IReadOnlyList<BackupEntry> List(string libraryRoot)
    {
        if (!Directory.Exists(libraryRoot))
        {
            return [];
        }

        List<BackupEntry> entries = [];

        foreach (string directoryPath in Directory.EnumerateDirectories(
                     libraryRoot,
                     BackupConst.BACKUP_DIRECTORY_PREFIX + "*"))
        {
            string snapshotPath = Path.Combine(directoryPath, BackupConst.SNAPSHOT_FILE_NAME);

            // 音声本体を複製しただけの過去の backup_* フォルダは対象外。
            if (!File.Exists(snapshotPath))
            {
                continue;
            }

            entries.Add(new BackupEntry(
                directoryPath,
                Path.GetFileName(directoryPath),
                TryLoadManifest(directoryPath)));
        }

        return [.. entries.OrderByDescending(entry => entry.DirectoryName, StringComparer.Ordinal)];
    }

    /// <summary>
    /// バックアップフォルダからスナップショットを読み込む。
    /// </summary>
    /// <param name="directoryPath">バックアップフォルダの絶対パス。</param>
    /// <returns>読み込んだスナップショット。</returns>
    /// <exception cref="FileNotFoundException">スナップショットが存在しない場合。</exception>
    /// <exception cref="InvalidDataException">JSON を読み取れない場合。</exception>
    public TagSnapshot Load(string directoryPath)
    {
        string snapshotPath = Path.Combine(directoryPath, BackupConst.SNAPSHOT_FILE_NAME);

        if (!File.Exists(snapshotPath))
        {
            throw new FileNotFoundException($"スナップショットが見つかりません: {snapshotPath}", snapshotPath);
        }

        using FileStream stream = File.OpenRead(snapshotPath);

        return JsonSerializer.Deserialize(stream, BackupJsonContext.Default.TagSnapshot)
            ?? throw new InvalidDataException($"スナップショットを読み取れません: {snapshotPath}");
    }

    /// <summary>
    /// マニフェストを読み込む。読めない場合は null を返す。
    /// </summary>
    private static BackupManifest? TryLoadManifest(string directoryPath)
    {
        string manifestPath = Path.Combine(directoryPath, BackupConst.MANIFEST_FILE_NAME);

        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(manifestPath);
            return JsonSerializer.Deserialize(stream, BackupJsonContext.Default.BackupManifest);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// 読み取ったタグをスナップショット用の形に変換する。
    /// </summary>
    private static SnapshotTrack ToSnapshotTrack(TrackTags track)
    {
        Dictionary<string, string[]> fields = [];

        foreach (TagField field in Enum.GetValues<TagField>())
        {
            IReadOnlyList<string> values = track.GetValues(field);

            if (values.Count > 0)
            {
                fields[field.ToString()] = [.. values];
            }
        }

        return new SnapshotTrack(
            track.RelativePath,
            track.Format.ToString(),
            fields,
            track.RawTags);
    }

    /// <summary>
    /// JSON をファイルへ書き出す。
    /// </summary>
    private static void WriteJson<T>(string path, T value)
    {
        string json = value switch
        {
            TagSnapshot snapshot => JsonSerializer.Serialize(snapshot, JSON_CONTEXT.TagSnapshot),
            BackupManifest manifest => JsonSerializer.Serialize(manifest, JSON_CONTEXT.BackupManifest),
            _ => throw new NotSupportedException($"シリアライズ対象外の型です: {typeof(T)}"),
        };

        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// 復元用 PowerShell と、それが使うタグライブラリをバックアップフォルダへ置く。
    /// </summary>
    private static void WriteRestoreScript(string directoryPath, string? portableLibraryPath)
    {
        Assembly assembly = typeof(SnapshotService).Assembly;

        string? resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(RESTORE_SCRIPT_RESOURCE_SUFFIX, StringComparison.Ordinal));

        if (resourceName is not null)
        {
            using Stream? resource = assembly.GetManifestResourceStream(resourceName);

            if (resource is not null)
            {
                using FileStream output = File.Create(
                    Path.Combine(directoryPath, BackupConst.RESTORE_SCRIPT_FILE_NAME));
                resource.CopyTo(output);
            }
        }

        if (portableLibraryPath is not null && File.Exists(portableLibraryPath))
        {
            File.Copy(
                portableLibraryPath,
                Path.Combine(directoryPath, Path.GetFileName(portableLibraryPath)),
                overwrite: true);
        }
    }

    /// <summary>
    /// バックアップフォルダ名から取得日時を読み取る。表示用。
    /// </summary>
    /// <param name="directoryName">フォルダ名。</param>
    /// <returns>取得日時。読み取れない場合は null。</returns>
    public static DateTimeOffset? ParseTimestamp(string directoryName)
    {
        if (!directoryName.StartsWith(BackupConst.BACKUP_DIRECTORY_PREFIX, StringComparison.Ordinal))
        {
            return null;
        }

        string timestamp = directoryName[BackupConst.BACKUP_DIRECTORY_PREFIX.Length..];

        return DateTimeOffset.TryParseExact(
            timestamp,
            BackupConst.BACKUP_DIRECTORY_TIMESTAMP_FORMAT,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out DateTimeOffset parsed)
            ? parsed
            : null;
    }
}
