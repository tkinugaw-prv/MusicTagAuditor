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

    /// <summary>同名フォルダに足す連番の開始値。</summary>
    private const int DIRECTORY_SEQUENCE_START = 2;

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
    /// 設定されたバックアップの保存先を取る手段。null または null を返す間は
    /// 従来どおりライブラリ直下に置く。
    /// </summary>
    private readonly Func<string?>? _backupRootProvider;

    /// <summary>
    /// スナップショットの取得・読み込みを行うサービスを作る。
    /// </summary>
    /// <param name="backupRootProvider">
    /// 設定されたバックアップの保存先を返す関数。
    /// **毎回呼び直す**ので、設定を変えた直後から新しい保存先が効く。
    /// 省略時はライブラリ直下（従来の動作）。
    /// </param>
    public SnapshotService(Func<string?>? backupRootProvider = null)
    {
        _backupRootProvider = backupRootProvider;
    }

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

        string directoryPath = CreateUniqueDirectory(ResolveBackupRoot(scan.LibraryRoot), createdAt);

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
    /// このライブラリのバックアップフォルダを新しい順に列挙する。
    ///
    /// **設定された保存先とライブラリ直下の両方を見る。** 保存先を変えたあとも、
    /// 以前ライブラリ直下に取ったバックアップが一覧から消えないようにするため。
    /// </summary>
    /// <param name="libraryRoot">ライブラリのルート。</param>
    /// <returns>バックアップの一覧。</returns>
    public IReadOnlyList<BackupEntry> List(string libraryRoot)
    {
        List<BackupEntry> entries = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        // ライブラリ直下。ここにあるものは、マニフェストが読めなくても対象ライブラリのものと分かる。
        Collect(libraryRoot, libraryRoot, requireMatchingManifest: false, seen, entries);

        string? backupRoot = _backupRootProvider?.Invoke();

        if (!string.IsNullOrWhiteSpace(backupRoot))
        {
            // 共有の保存先には他のライブラリのバックアップも混ざりうるので、
            // マニフェストで対象ライブラリを確かめられたものだけ採る。
            Collect(backupRoot, libraryRoot, requireMatchingManifest: true, seen, entries);
        }

        // 保存先が 2 箇所になると同名フォルダが並びうるため、フォルダ名ではなく取得日時で並べる。
        return
        [
            .. entries
                .OrderByDescending(GetCreatedAt)
                .ThenByDescending(entry => entry.DirectoryName, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// 1 つのフォルダ配下からバックアップを集める。
    /// </summary>
    /// <param name="searchRoot">探すフォルダ。</param>
    /// <param name="libraryRoot">対象ライブラリのルート。</param>
    /// <param name="requireMatchingManifest">
    /// マニフェストで対象ライブラリを確認できたものだけ採るか。
    /// </param>
    /// <param name="seen">採用済みのフォルダパス。保存先がライブラリ直下と重なる場合の二重採用を防ぐ。</param>
    /// <param name="entries">集めた結果の追加先。</param>
    private static void Collect(
        string searchRoot,
        string libraryRoot,
        bool requireMatchingManifest,
        HashSet<string> seen,
        List<BackupEntry> entries)
    {
        if (!Directory.Exists(searchRoot))
        {
            return;
        }

        foreach (string directoryPath in Directory.EnumerateDirectories(
                     searchRoot,
                     BackupConst.BACKUP_DIRECTORY_PREFIX + "*"))
        {
            // 音声本体を複製しただけの過去の backup_* フォルダは対象外。
            if (!File.Exists(Path.Combine(directoryPath, BackupConst.SNAPSHOT_FILE_NAME)))
            {
                continue;
            }

            if (!seen.Add(Path.GetFullPath(directoryPath)))
            {
                continue;
            }

            BackupManifest? manifest = TryLoadManifest(directoryPath);

            if (requireMatchingManifest
                && (manifest is null || !IsSameDirectory(manifest.LibraryRoot, libraryRoot)))
            {
                continue;
            }

            entries.Add(new BackupEntry(directoryPath, Path.GetFileName(directoryPath), manifest));
        }
    }

    /// <summary>
    /// 並べ替えに使う取得日時。マニフェストが読めない場合はフォルダ名から読む。
    /// </summary>
    private static DateTimeOffset GetCreatedAt(BackupEntry entry)
    {
        return entry.Manifest?.CreatedAt
            ?? ParseTimestamp(entry.DirectoryName)
            ?? DateTimeOffset.MinValue;
    }

    /// <summary>
    /// 2 つのパスが同じフォルダを指すかを判定する。末尾の区切り文字と大小文字の差は無視する。
    /// </summary>
    private static bool IsSameDirectory(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

        static string Normalize(string path)
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
    }

    /// <summary>
    /// バックアップの出力先フォルダを決める。設定が無ければライブラリ直下。
    /// </summary>
    private string ResolveBackupRoot(string libraryRoot)
    {
        string? configured = _backupRootProvider?.Invoke();

        return string.IsNullOrWhiteSpace(configured) ? libraryRoot : configured;
    }

    /// <summary>
    /// バックアップフォルダを作る。同名が既にあれば連番を足す。
    ///
    /// 保存先を複数ライブラリで共有すると、同じ秒に取ったバックアップ同士が
    /// ぶつかりうる（ライブラリごとに分かれていた頃は起こらなかった）。
    /// </summary>
    /// <param name="backupRoot">出力先の親フォルダ。</param>
    /// <param name="createdAt">取得日時。</param>
    /// <returns>作成したフォルダの絶対パス。</returns>
    private static string CreateUniqueDirectory(string backupRoot, DateTimeOffset createdAt)
    {
        string baseName = BackupConst.BuildDirectoryName(createdAt);
        string candidate = Path.Combine(backupRoot, baseName);

        for (int sequence = DIRECTORY_SEQUENCE_START; Directory.Exists(candidate); sequence++)
        {
            candidate = Path.Combine(
                backupRoot,
                BackupConst.AppendSequence(baseName, sequence));
        }

        Directory.CreateDirectory(candidate);

        return candidate;
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
    /// 連番付き（<c>backup_20260803031500_2</c>）でも読める。
    /// </summary>
    /// <param name="directoryName">フォルダ名。</param>
    /// <returns>取得日時。読み取れない場合は null。</returns>
    public static DateTimeOffset? ParseTimestamp(string directoryName)
    {
        ArgumentNullException.ThrowIfNull(directoryName);

        if (!directoryName.StartsWith(BackupConst.BACKUP_DIRECTORY_PREFIX, StringComparison.Ordinal))
        {
            return null;
        }

        string remainder = directoryName[BackupConst.BACKUP_DIRECTORY_PREFIX.Length..];

        // 衝突回避の連番は日時の後ろに付く。日時部分だけを取り出す。
        int separator = remainder.IndexOf(BackupConst.SEQUENCE_SEPARATOR, StringComparison.Ordinal);
        string timestamp = separator >= 0 ? remainder[..separator] : remainder;

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
