using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Editing;

/// <summary>
/// 手編集で使う定数（docs/SPEC.md 5.2 / 12章 段階 6）。
/// docs/llm_guideline.md の規約により、定数は FULL_CAPITAL で定義する。
/// </summary>
public static class ManualEditConst
{
    /// <summary>
    /// 手編集の差分に付けるルール ID。
    ///
    /// 検査ルールと同じ <see cref="TagChange"/> で表すのは、適用フロー（自動バックアップ →
    /// 書き込み → 読み戻し照合 → 失敗一覧）を共用するため。書き込み経路を 2 本持たない。
    /// </summary>
    public const string RULE_ID = "MANUAL";

    /// <summary>手編集の差分に付ける既定の根拠。**根拠が空の差分を作らない**（docs/SPEC.md 5.3）。</summary>
    public const string RATIONALE_SINGLE = "手編集";

    /// <summary>
    /// 手で編集できるフィールド。
    ///
    /// ファイル名・フォルダ名の変更は対象外（docs/SPEC.md 2.2。v2 で検討）。
    /// </summary>
    public static readonly IReadOnlyList<TagField> EDITABLE_FIELDS =
    [
        TagField.Title,
        TagField.Artist,
        TagField.AlbumArtist,
        TagField.Composer,
        TagField.Conductor,
        TagField.Album,
        TagField.Genre,
        TagField.Date,
        TagField.TrackNumber,
        TagField.DiscNumber,
        TagField.Comment,
    ];

    /// <summary>
    /// 空欄かどうかを見るフィールド。「空欄のある行のみ」の絞り込みに使う。
    ///
    /// 自由記述のフィールドを外しているのは、<c>comment</c> が大半のファイルで空であるのが
    /// 正常だからである（版・稿の注記を置く欄で、必要なファイルにしか入らない）。
    /// 含めるとほぼ全行が「空欄あり」になり、R-401 / R-402 の対象を探すという
    /// この絞り込みの目的が果たせなくなる。
    /// </summary>
    public static readonly IReadOnlyList<TagField> EMPTY_CHECK_FIELDS =
        [.. EDITABLE_FIELDS.Where(field => !TagFieldConst.IsFreeText(field))];

    /// <summary>フィールドの日本語表示名。一括入力の選択肢と警告文に使う。</summary>
    public static readonly IReadOnlyDictionary<TagField, string> FIELD_LABELS =
        new Dictionary<TagField, string>
        {
            [TagField.Title] = "タイトル",
            [TagField.Artist] = "アーティスト",
            [TagField.AlbumArtist] = "アルバムアーティスト",
            [TagField.Composer] = "作曲家",
            [TagField.Conductor] = "指揮者",
            [TagField.Album] = "アルバム",
            [TagField.Genre] = "ジャンル",
            [TagField.Date] = "年",
            [TagField.TrackNumber] = "トラック",
            [TagField.DiscNumber] = "ディスク",
            [TagField.Comment] = "コメント",
        };

    /// <summary>
    /// フィールドの表示名を返す。
    /// </summary>
    /// <param name="field">対象フィールド。</param>
    /// <returns>表示名。</returns>
    public static string Label(TagField field)
    {
        return FIELD_LABELS.TryGetValue(field, out string? label) ? label : field.ToString();
    }
}
