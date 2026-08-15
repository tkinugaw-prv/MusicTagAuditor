using System.Collections.Frozen;

namespace MusicTagAuditor.Core.Models;

/// <summary>
/// <see cref="TagField"/> の性質。フィールドを足したらここで分類を決める。
/// docs/llm_guideline.md の規約により、定数は FULL_CAPITAL で定義する。
///
/// 検査対象・空欄検査の対象・辞書候補の有無は、いずれも「そのフィールドが自由記述か」から
/// 導ける。個々の機能ごとに一覧を持つと、フィールドを足すたびに複数箇所を直すことになり、
/// 1 箇所だけ忘れるという同じ事故が形を変えて再発する。性質はここに 1 つだけ置く。
/// </summary>
public static class TagFieldConst
{
    /// <summary>
    /// 自由記述のフィールド。正規形を定めず、検査ルールの対象にしない
    /// （docs/TAGGING_POLICY.md 2.4）。
    /// </summary>
    public static readonly FrozenSet<TagField> FREE_TEXT_FIELDS =
        new[] { TagField.Comment }.ToFrozenSet();

    /// <summary>
    /// 形式ごとに扱わないフィールド。ここに無い組み合わせはすべて扱える。
    ///
    /// ID3v2 の <c>COMM</c> は、iTunes が <c>iTunNORM</c> / <c>iTunSMPB</c> /
    /// <c>iTunes_CDDB_IDs</c> を description 付きで格納する場所でもある。論理フィールドとして
    /// 素直に読み書きすると、読みではそのバイナリ文字列を拾い、値を空にしたときの
    /// <c>RemoveFrames</c> が音量正規化情報を巻き添えで消す。実測（2026-08-15）では
    /// 対象ライブラリの AIFF 11 件すべてが description 付きの <c>COMM</c> を 3 個ずつ持ち、
    /// 利用者のコメント（description が空のもの）は 1 件も無かった。
    /// 得るもののほうが小さいため ID3 では扱わない（docs/TAGGING_POLICY.md 4.4）。
    /// </summary>
    private static readonly FrozenDictionary<AudioFormat, FrozenSet<TagField>> UNSUPPORTED_BY_FORMAT =
        new Dictionary<AudioFormat, FrozenSet<TagField>>
        {
            [AudioFormat.Id3] = new[] { TagField.Comment }.ToFrozenSet(),
        }.ToFrozenDictionary();

    /// <summary>
    /// 自由記述のフィールドかを返す。
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <returns>自由記述なら true。</returns>
    public static bool IsFreeText(TagField field)
    {
        return FREE_TEXT_FIELDS.Contains(field);
    }

    /// <summary>
    /// その形式でフィールドを読み書きできるかを返す。
    ///
    /// 書き込み層は対応表に無いフィールドを黙って無視するため、扱えない組み合わせを
    /// 編集しても適用時に何も起きない。画面側はこの判定で利用者に知らせる。
    /// </summary>
    /// <param name="format">タグの格納形式。</param>
    /// <param name="field">対象フィールド。</param>
    /// <returns>扱えるなら true。</returns>
    public static bool IsSupported(AudioFormat format, TagField field)
    {
        return !UNSUPPORTED_BY_FORMAT.TryGetValue(format, out FrozenSet<TagField>? fields)
            || !fields.Contains(field);
    }
}
