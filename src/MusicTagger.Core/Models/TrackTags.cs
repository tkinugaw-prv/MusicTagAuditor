using System.Collections.Frozen;

namespace MusicTagger.Core.Models;

/// <summary>
/// 1 ファイルから読み取ったタグ一式。読み取り時点のスナップショットであり不変。
/// 編集は本型を書き換えるのではなく <c>TagChange</c> の集合として表現する（docs/SPEC.md 10章）。
/// </summary>
public sealed record TrackTags
{
    /// <summary>複数値を 1 つの文字列にまとめるときの区切り。</summary>
    public const string VALUE_JOIN_SEPARATOR = "; ";

    /// <summary>ライブラリルートからの相対パス。</summary>
    public required string RelativePath { get; init; }

    /// <summary>ファイルの絶対パス。</summary>
    public required string FullPath { get; init; }

    /// <summary>タグの格納形式。</summary>
    public required AudioFormat Format { get; init; }

    /// <summary>
    /// 論理フィールドごとに、ファイルに実際に格納されていた値を並べたもの。
    /// 値が 2 つ以上あるのは AIMP が <c>;</c> で分割した状態であり、検査ルールが検出する対象になる
    /// （docs/TAGGING_POLICY.md 3.4）。そのため 1 値へ丸めずに保持する。
    /// </summary>
    public required IReadOnlyDictionary<TagField, IReadOnlyList<string>> Fields { get; init; }

    /// <summary>
    /// 読み取ったすべての生タグ。編集対象でないタグ（<c>iTunNORM</c>、<c>iTunSMPB</c>、CDDB ID 等）を
    /// 保存時に失わないために必須（docs/SPEC.md 10章）。
    /// </summary>
    public required IReadOnlyDictionary<string, string[]> RawTags { get; init; }

    /// <summary>曲名。</summary>
    public string? Title => GetSingle(TagField.Title);

    /// <summary>その録音の主役。</summary>
    public string? Artist => GetSingle(TagField.Artist);

    /// <summary>演奏団体。</summary>
    public string? AlbumArtist => GetSingle(TagField.AlbumArtist);

    /// <summary>作曲家。</summary>
    public string? Composer => GetSingle(TagField.Composer);

    /// <summary>指揮者。</summary>
    public string? Conductor => GetSingle(TagField.Conductor);

    /// <summary>アルバム名。</summary>
    public string? Album => GetSingle(TagField.Album);

    /// <summary>ジャンル。</summary>
    public string? Genre => GetSingle(TagField.Genre);

    /// <summary>録音年。</summary>
    public string? Date => GetSingle(TagField.Date);

    /// <summary>トラック番号。</summary>
    public string? TrackNumber => GetSingle(TagField.TrackNumber);

    /// <summary>ディスク番号。</summary>
    public string? DiscNumber => GetSingle(TagField.DiscNumber);

    /// <summary>
    /// 指定フィールドに格納されていた値をすべて返す。
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <returns>格納値。未設定なら空。</returns>
    public IReadOnlyList<string> GetValues(TagField field)
    {
        return Fields.TryGetValue(field, out IReadOnlyList<string>? values) ? values : [];
    }

    /// <summary>
    /// 指定フィールドを表示用の 1 文字列にして返す。
    /// 複数値の場合は連結する。連結されたこと自体は <see cref="HasMultipleValues"/> で判別する。
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <returns>値。未設定なら null。</returns>
    public string? GetSingle(TagField field)
    {
        IReadOnlyList<string> values = GetValues(field);

        return values.Count switch
        {
            0 => null,
            1 => values[0],
            _ => string.Join(VALUE_JOIN_SEPARATOR, values),
        };
    }

    /// <summary>
    /// 指定フィールドが複数値として格納されているかを返す。
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <returns>格納値が 2 つ以上なら true。</returns>
    public bool HasMultipleValues(TagField field)
    {
        return GetValues(field).Count > 1;
    }

    /// <summary>
    /// フィールド辞書を、空文字を除いたうえで読み取り専用にして作る。
    /// </summary>
    /// <param name="source">フィールドと値の組。</param>
    /// <returns>読み取り専用のフィールド辞書。</returns>
    public static IReadOnlyDictionary<TagField, IReadOnlyList<string>> BuildFields(
        IEnumerable<KeyValuePair<TagField, IReadOnlyList<string>>> source)
    {
        Dictionary<TagField, IReadOnlyList<string>> fields = [];

        foreach ((TagField field, IReadOnlyList<string> values) in source)
        {
            string[] cleaned = [.. values.Where(value => !string.IsNullOrWhiteSpace(value))];
            if (cleaned.Length > 0)
            {
                fields[field] = cleaned;
            }
        }

        return fields.ToFrozenDictionary();
    }
}
