using System.Text.Json.Serialization;

namespace MusicTagger.Core.Backup;

/// <summary>
/// スナップショットを取った理由。
/// </summary>
public enum SnapshotReason
{
    /// <summary>利用者が明示的に取得した。</summary>
    Manual,

    /// <summary>差分の適用直前に自動で取得した（docs/SPEC.md 8.2）。</summary>
    BeforeApply,

    /// <summary>復元の直前に自動で取得した。復元自体を巻き戻せるようにするため。</summary>
    BeforeRestore,
}

/// <summary>
/// スナップショットに記録する 1 ファイル分のタグ。
/// </summary>
/// <param name="Path">ライブラリルートからの相対パス。</param>
/// <param name="Format">タグの格納形式。</param>
/// <param name="Fields">
/// 論理フィールドと値。**復元はこの内容だけを使う。**
/// 値が配列なのは、AIMP が <c>;</c> で分割した状態をそのまま記録するため。
/// </param>
/// <param name="RawTags">読み取れたすべての生タグ。記録用であり復元には使わない。</param>
public sealed record SnapshotTrack(
    string Path,
    string Format,
    IReadOnlyDictionary<string, string[]> Fields,
    IReadOnlyDictionary<string, string[]> RawTags);

/// <summary>
/// タグのスナップショット。音声ファイル本体は複製しない（対象ライブラリは 30GB。docs/SPEC.md 8.1）。
///
/// **アプリが無くても復元できる構造にしてある。** 同じフォルダに置かれる
/// <c>restore-tags.ps1</c> がこの JSON だけを見て復元できる。
/// </summary>
/// <param name="Version">スキーマ版。</param>
/// <param name="CreatedAt">取得日時。</param>
/// <param name="LibraryRoot">対象ライブラリのルート。</param>
/// <param name="TrackCount">記録したファイル数。</param>
/// <param name="Tracks">ファイルごとのタグ。</param>
public sealed record TagSnapshot(
    int Version,
    DateTimeOffset CreatedAt,
    string LibraryRoot,
    int TrackCount,
    IReadOnlyList<SnapshotTrack> Tracks);

/// <summary>
/// スナップショットに添える情報。何のために取ったバックアップかを後から追えるようにする。
/// </summary>
/// <param name="Version">スキーマ版。</param>
/// <param name="CreatedAt">取得日時。</param>
/// <param name="Reason">取得理由。</param>
/// <param name="LibraryRoot">対象ライブラリのルート。</param>
/// <param name="TrackCount">記録したファイル数。</param>
/// <param name="ReadFailureCount">スキャン時に読み取れなかったファイル数。</param>
/// <param name="Note">補足。適用直前のスナップショットでは適用内容の要約を入れる。</param>
public sealed record BackupManifest(
    int Version,
    DateTimeOffset CreatedAt,
    string Reason,
    string LibraryRoot,
    int TrackCount,
    int ReadFailureCount,
    string? Note);

/// <summary>
/// スナップショット JSON のシリアライズ設定。
/// System.Text.Json のソース生成を使う（docs/SPEC.md 3章）。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TagSnapshot))]
[JsonSerializable(typeof(BackupManifest))]
public sealed partial class BackupJsonContext : JsonSerializerContext
{
}
