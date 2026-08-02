namespace MusicTagger.Core.Scanning;

/// <summary>
/// スキャンの設定。対象拡張子を将来追加できるよう、既定値を上書きできる形にしてある
/// （docs/SPEC.md 11章「将来 .wav .ogg .opus を追加可能な構造にする」）。
/// </summary>
public sealed class ScanOptions
{
    /// <summary>
    /// 除外するフォルダ名の接頭辞。
    /// バックアップフォルダは音声本体の複製を含むことがあり、スキャンすると二重に数えてしまう
    /// （docs/SPEC.md 11章）。
    /// </summary>
    public const string EXCLUDED_DIRECTORY_PREFIX = "backup_";

    /// <summary>既定の対象拡張子。</summary>
    public static readonly string[] DEFAULT_EXTENSIONS = [".m4a", ".flac", ".mp3", ".aif", ".aiff"];

    /// <summary>対象とする拡張子。</summary>
    public IReadOnlyList<string> Extensions { get; init; } = DEFAULT_EXTENSIONS;

    /// <summary>
    /// 並列読み取りの多重度。0 以下ならプロセッサ数を使う。
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; }
}
