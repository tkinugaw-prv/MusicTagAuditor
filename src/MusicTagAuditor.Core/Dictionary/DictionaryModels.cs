using System.Text.Json;
using System.Text.Json.Serialization;

namespace MusicTagAuditor.Core.Dictionary;

/// <summary>
/// 人物が担う役割。1 人が複数の役割を持つ（例: アシュケナージはソリストでも指揮者でもある）。
/// </summary>
public enum PersonRole
{
    /// <summary>指揮者。</summary>
    Conductor,

    /// <summary>ソリスト・独奏者（docs/TAGGING_POLICY.md 2.2）。</summary>
    Soloist,
}

/// <summary>
/// 作曲家 1 人分の正規形とエイリアス。
///
/// 配列は JSON で省略できるよう既定値を持たせてある。省略時に null になると
/// 索引の構築で落ちるため、ここで空配列に寄せる。
/// </summary>
public sealed record ComposerEntry
{
    /// <summary>正規形。</summary>
    public string Canonical { get; init; } = string.Empty;

    /// <summary>ラテン文字の別表記・誤記。</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>日本語表記。フォルダ名からの推定に使う。</summary>
    public IReadOnlyList<string> AliasesJa { get; init; } = [];
}

/// <summary>
/// 人物 1 人分の正規形とエイリアス。
///
/// docs/SPEC.md 7.2 の <c>conductors</c> を一般化したもの。
/// 2.2 の「協奏曲は <c>artist</c> にソリスト」を扱うには指揮者以外も辞書に要るため、
/// 役割を複数持てる形にしてある。
/// </summary>
public sealed record PersonEntry
{
    /// <summary>正規形。</summary>
    public string Canonical { get; init; } = string.Empty;

    /// <summary>担う役割。<see cref="PersonRole"/> の名前を入れる。</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>ラテン文字の別表記・誤記。</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>日本語表記。</summary>
    public IReadOnlyList<string> AliasesJa { get; init; } = [];
}

/// <summary>
/// 団体名の時代区分 1 つ分（docs/TAGGING_POLICY.md 5.3.1）。
/// </summary>
public sealed record EnsembleEra
{
    /// <summary>この年以降に適用する。null なら下限なし。</summary>
    public int? From { get; init; }

    /// <summary>この年より前に適用する。null なら上限なし。</summary>
    public int? Until { get; init; }

    /// <summary>その期間の正規形。</summary>
    public string Canonical { get; init; } = string.Empty;
}

/// <summary>
/// 演奏団体 1 つ分。
///
/// **同一性は <see cref="EntityId"/> で判断する。名前の類似で束ねてはならない**
/// （docs/TAGGING_POLICY.md 5.3.1）。
/// </summary>
public sealed record EnsembleEntry
{
    /// <summary>実体 ID。改名をまたいで同一の団体を指す。</summary>
    public string EntityId { get; init; } = string.Empty;

    /// <summary>時代分割しない場合の正規形。</summary>
    public string? Canonical { get; init; }

    /// <summary>録音年によって変わる正規形。空なら <see cref="Canonical"/> を使う。</summary>
    public IReadOnlyList<EnsembleEra> Eras { get; init; } = [];

    /// <summary>時代分割を行わない個別例外（5.3.2）。</summary>
    public bool NoEraSplit { get; init; }

    /// <summary>
    /// 指揮者を置かない団体か（合奏団・弦楽四重奏団など）。
    ///
    /// この団体の録音では <c>conductor</c> が空なのが正しい（docs/TAGGING_POLICY.md 2.2）。
    /// **立てないと R-402 が誤検出する。** 実ライブラリでは I Musici と Smetana Quartet が該当し、
    /// 合わせて 22 件になる（docs/library-baseline-2026-08-03.md）。
    /// </summary>
    public bool NoConductor { get; init; }

    /// <summary>ラテン文字の別表記・誤記。</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>日本語表記。</summary>
    public IReadOnlyList<string> AliasesJa { get; init; } = [];
}

/// <summary>
/// 楽語のスペルミス 1 件（docs/TAGGING_POLICY.md 5.4）。
///
/// **照合は正規表現で行う。** 完全一致だと区切り文字違いを取りこぼす。
/// **団体名には適用しない**（<c>Münchener Bach-Chor</c> の -ener は正しい表記）。
/// </summary>
public sealed record TypoEntry
{
    /// <summary>検出する正規表現。</summary>
    public string Pattern { get; init; } = string.Empty;

    /// <summary>置換後の文字列。</summary>
    public string Replacement { get; init; } = string.Empty;

    /// <summary>由来のメモ。</summary>
    public string? Note { get; init; }
}

/// <summary>
/// 正規化辞書。
/// </summary>
public sealed record TagDictionary
{
    /// <summary>スキーマ版。</summary>
    public int Version { get; init; }

    /// <summary>作曲家。</summary>
    public IReadOnlyList<ComposerEntry> Composers { get; init; } = [];

    /// <summary>指揮者・ソリスト。</summary>
    public IReadOnlyList<PersonEntry> Persons { get; init; } = [];

    /// <summary>演奏団体。</summary>
    public IReadOnlyList<EnsembleEntry> Ensembles { get; init; } = [];

    /// <summary>楽語のスペルミス。</summary>
    public IReadOnlyList<TypoEntry> Typos { get; init; } = [];

    /// <summary>
    /// 配役情報を含むため書き換えない <c>albumartist</c>（docs/TAGGING_POLICY.md 2.3）。
    /// **検査そのものから除外する。** 除外しないと R-207 / R-208 が誤検出だらけになる。
    /// </summary>
    public IReadOnlyList<string> ProtectedAlbumArtists { get; init; } = [];

    /// <summary>
    /// モデルに無いプロパティ（<c>_comment</c> 等）をそのまま保持する。
    ///
    /// 同梱辞書は「推測で名前を足さないこと」といった注意書きを <c>_</c> 始まりの
    /// プロパティとして持っている。**書き出しでこれを落とすと、辞書を編集する人が
    /// 前提を知らないまま値を足せるようになる。** 読み書きで往復させて残す。
    /// なお書き出し位置は末尾になる（拡張データは通常プロパティの後に出力されるため）。
    /// </summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extensions { get; set; }
}

/// <summary>
/// 辞書 JSON のシリアライズ設定。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TagDictionary))]
public sealed partial class DictionaryJsonContext : JsonSerializerContext
{
}
