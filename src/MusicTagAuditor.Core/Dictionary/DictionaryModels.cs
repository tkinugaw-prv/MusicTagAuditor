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
/// 作品 1 つ分（docs/SPEC.md 7.4）。
///
/// <c>TAGGING_POLICY.md</c> 3.5 のアルバム名 <c>{作曲家}: {作品名} - {date}/{artist}</c> における
/// <c>{作品名}</c> の唯一の供給元。<c>title</c>（楽章名）からも <c>album</c>（汎用名）からも
/// 一意には取れないため、辞書に持つ。
///
/// **自然キーは <see cref="Composer"/> + <see cref="Canonical"/> の組。** 別途 ID は持たない。
/// 団体（<see cref="EnsembleEntry.EntityId"/>）は改名をまたいで同一性を保つ必要があるが、
/// 作品名は改名しないため同じ仕掛けは要らない。
///
/// **1 作品 1 エントリ。版で分けない**（3.5 規則4）。版・稿の情報は <c>comment</c> に置き、
/// 版だけが違う録音を所蔵した場合はそのアルバムだけ <see cref="AlbumOverrideEntry"/> で例外にする。
/// </summary>
public sealed record WorkEntry
{
    /// <summary>
    /// この作品の作曲家。<c>composers</c> の正規形と一致させる。
    ///
    /// **引くときのキーの一部になる。** <c>Symphony No.5</c> は作曲家が違えば別の作品であり、
    /// 作曲家で絞らずに引くと R-501 が検出している衝突をそのまま再生産する。
    /// </summary>
    public string Composer { get; init; } = string.Empty;

    /// <summary>
    /// 作品名。アルバム名にそのまま入る値。
    /// 言語は「ジャンル名は英語・固有の題名は原語」（docs/TAGGING_POLICY.md 3.5 規則8）。
    /// </summary>
    public string Canonical { get; init; } = string.Empty;

    /// <summary>ラテン文字の別表記。現在の <c>album</c> の値・フォルダ名のうちラテン文字のもの。</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>日本語表記。略称（<c>ベト7</c>）と正式な日本語名（<c>交響曲第8番</c>）の両方。</summary>
    public IReadOnlyList<string> AliasesJa { get; init; } = [];
}

/// <summary>
/// アルバム単位の個別例外 1 件（docs/SPEC.md 7.4.5）。
///
/// docs/TAGGING_POLICY.md 3.5 の規則4（版の違い）・規則6（コンピレーション）・規則7（同一演奏の
/// 別リリース）は、いずれも「そのアルバムだけ」の例外を認めている。**規則そのものを一般化しない**
/// ため、作品エントリではなくアルバム単位に紐づける。
/// </summary>
public sealed record AlbumOverrideEntry
{
    /// <summary>ライブラリルートからの相対パス。</summary>
    public string Folder { get; init; } = string.Empty;

    /// <summary>対象のディスク番号。null ならそのフォルダの全ディスク。</summary>
    public int? Disc { get; init; }

    /// <summary>単位の作曲家を明示する。主作品 + カップリング（3.5 規則5）で使う。</summary>
    public string? Composer { get; init; }

    /// <summary>作品名を明示する。版の違い（規則4）・同一演奏の別リリース（規則7）で使う。</summary>
    public string? WorkName { get; init; }

    /// <summary>アルバム名の対象外にするか。本物のコンピレーション（規則6）で使う。</summary>
    public bool Exclude { get; init; }

    /// <summary>例外の理由。**書く運用とする。** 理由の無い例外は後から消せない。</summary>
    public string? Note { get; init; }
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

    /// <summary>
    /// 作品（docs/SPEC.md 7.4）。アルバム名の <c>{作品名}</c> の供給元。
    /// **同梱の既定辞書には入れない。** 所蔵に完全に依存するため（13章 D5）。
    /// </summary>
    public IReadOnlyList<WorkEntry> Works { get; init; } = [];

    /// <summary>アルバム単位の個別例外（docs/SPEC.md 7.4.5）。</summary>
    public IReadOnlyList<AlbumOverrideEntry> AlbumOverrides { get; init; } = [];

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
