using System.Text.RegularExpressions;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Inspection.Rules;

/// <summary>
/// R-504: <c>album</c> が 3.5 の書式と不一致（docs/TAGGING_POLICY.md 3.5 / docs/SPEC.md 7.4）。
///
/// 書式は <c>{作曲家}: {作品名} - {date}/{artist}</c>。**アルバム単位（フォルダ + <c>discnumber</c>）で
/// 判定し、単位内の全ファイルへ同じ値を出す。** ファイル単位で判定すると、複数ディスク・複数フォルダに
/// 分かれた 1 アルバムが割れる（3.5 規則3）。
///
/// 決められない要素があれば**保留**にする。保留は適用対象外で、条件が揃えば自動的に再判定できる
/// （SPEC 7.4.4）。**推測で埋めない**（7章 原則4）。
/// </summary>
public sealed class AlbumNameRule : IInspectionRule
{
    /// <summary>ルール ID。検査結果からの導線（docs/SPEC.md 7.3.2）が対象を選ぶのに使う。</summary>
    public const string RULE_ID = "R-504";

    /// <inheritdoc />
    public string Id => RULE_ID;

    /// <inheritdoc />
    public Severity Severity => Severity.Warning;

    /// <inheritdoc />
    public string Description => "album が「作曲家: 作品名 - 年/演奏者」形式と不一致";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (AlbumUnit unit in context.Units)
        {
            foreach (TagChange change in InspectUnit(unit, context))
            {
                yield return change;
            }
        }
    }

    /// <summary>
    /// 単位 1 つを判定し、単位内の全ファイル分の修正案を返す。
    /// </summary>
    private static IEnumerable<TagChange> InspectUnit(AlbumUnit unit, InspectionContext context)
    {
        AlbumOverrideEntry? nameOverride = context.Dictionary
            .TryResolveAlbumOverride(unit.Folder, unit.Disc, out AlbumOverrideEntry found) ? found : null;

        // 規則6 の対象外。主作品が定まらないアルバムは一覧にも出さない。
        // 「直す必要があるのに出ていない」ではなく「対象外だと決めた」ものなので、検出にしない。
        if (nameOverride?.Exclude == true)
        {
            return [];
        }

        string? composer = nameOverride?.Composer ?? Single(unit.Composers);

        if (composer is null)
        {
            return Hold(unit, HoldReason.WorkUnknown, unit.Composers.Count == 0
                ? "composer が未設定のためアルバム名を決められない"
                : $"単位内に作曲家が {unit.Composers.Count} 人いる（{string.Join(" / ", unit.Composers)}）。"
                    + "この行を選んで「このアルバムの扱いを決める」を押し、"
                    + "主作品が定まるなら作曲家を指定する（3.5 規則5）。定まらないなら対象外にする（規則6）");
        }

        string workSource = $"個別例外で作品名を「{nameOverride?.WorkName}」と指定（{nameOverride?.Note}）";
        string? work = nameOverride?.WorkName;

        if (work is null)
        {
            work = FindWork(unit, context, composer, out workSource);

            if (work is null)
            {
                return Hold(unit, HoldReason.WorkUnknown, workSource);
            }
        }

        string? date = Single(unit.Dates);

        if (date is null)
        {
            // 保留の理由だけでは、次に何をすればいいのかが画面から分からない。**個別例外では解けない
            // 保留である**ことも伝わらず、「このアルバムの扱いを決める」で対象外にして消す誤操作を招く。
            // それはタグが割れたまま一覧から消えるだけで、規則2 の保留を規則6 で握り潰すことになる。
            return Hold(unit, HoldReason.DateUnknown, unit.Dates.Count == 0
                ? "date が未設定のため年を決められない。推測で埋めない。"
                    + "CD 実物で録音年を確かめ、ファイル一覧タブで date を入れる（3.5 規則2）"
                : $"単位内で date が割れている（{string.Join(" / ", unit.Dates)}）。"
                    + "別々の録音が 1 つのフォルダに入っているならフォルダを分ける。"
                    + "同じ録音なら、この行をダブルクリックしてファイル一覧タブで date を揃える（3.5 規則2）");
        }

        string? artist = Single(unit.Artists);

        if (artist is null)
        {
            // date と同じ理由で、直し方まで書く。こちらも個別例外では解けない。
            return Hold(unit, HoldReason.ArtistUnknown, unit.Artists.Count == 0
                ? "artist が未設定のため演奏者を決められない。"
                    + "CD 実物で演奏者を確かめ、ファイル一覧タブで artist を入れる"
                : $"単位内で artist が割れている（{string.Join(" / ", unit.Artists)}）。"
                    + "別々の演奏が 1 つのフォルダに入っているならフォルダを分ける。"
                    + "同じ演奏なら、この行をダブルクリックしてファイル一覧タブで artist を揃える");
        }

        string album = $"{composer}: {work} - {date}/{artist}";

        return
        [
            .. unit.Tracks
                .Where(track => !string.Equals(track.Album, album, StringComparison.Ordinal))
                .Select(track => new TagChange(
                    track.RelativePath,
                    TagField.Album,
                    track.GetValues(TagField.Album),
                    [album],
                    RULE_ID,
                    $"{workSource}。3.5 の書式で組み立てた",
                    Severity.Warning)),
        ];
    }

    /// <summary>
    /// 作品エントリを引く（docs/SPEC.md 7.4.3 手順4・5）。
    ///
    /// **<c>album</c> の値とフォルダ名の両方から引き、食い違ったら諦める。**<c>album</c> は
    /// 誤っているファイルが実在するため単独では信用しない（3.5 補足2）。
    /// </summary>
    /// <param name="unit">アルバム単位。</param>
    /// <param name="context">ライブラリ全体の文脈。</param>
    /// <param name="composer">単位の作曲家。</param>
    /// <param name="source">根拠、または諦めた理由。</param>
    /// <returns>作品名。決められなければ null。</returns>
    private static string? FindWork(AlbumUnit unit, InspectionContext context, string composer, out string source)
    {
        string? fromAlbum = null;

        foreach (string album in unit.Albums)
        {
            if (context.Dictionary.TryResolveWork(composer, album, out WorkEntry entry))
            {
                fromAlbum = entry.Canonical;
                break;
            }
        }

        string? fromFolder = FindWorkInFolder(unit.Folder, context, composer);

        if (fromAlbum is not null && fromFolder is not null)
        {
            if (!string.Equals(fromAlbum, fromFolder, StringComparison.Ordinal))
            {
                source = $"album からは「{fromAlbum}」、フォルダ名からは「{fromFolder}」と読める。"
                    + "どちらかのタグが誤っている可能性があるため決められない";

                return null;
            }

            source = $"album とフォルダ名の両方が作品「{fromAlbum}」を指す";
            return fromAlbum;
        }

        if (fromAlbum is not null)
        {
            source = $"album「{unit.Albums[0]}」から作品「{fromAlbum}」を特定";
            return fromAlbum;
        }

        if (fromFolder is not null)
        {
            source = $"フォルダ名から作品「{fromFolder}」を特定";
            return fromFolder;
        }

        source = $"作曲家「{composer}」の作品エントリに一致する手がかりが無い。辞書に作品を足す（SPEC 7.4）";
        return null;
    }

    /// <summary>
    /// フォルダ名から作品を引く。
    ///
    /// **末端のセグメントから親へ遡る。** 作品名が親フォルダにしか出てこない構成があるため
    /// （`ワーグナー\ワルキューレ\第二幕`。3.5 規則3 の B 種別）。
    /// セグメントは全体と「最初の <c>-</c> より前」の 2 通りで引く。フォルダ名には演奏者が
    /// 付いていることが多い（`ブルックナー 8 - Wand`）。
    /// </summary>
    private static string? FindWorkInFolder(string folder, InspectionContext context, string composer)
    {
        string[] segments = folder.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string segment in segments.Reverse())
        {
            if (context.Dictionary.TryResolveWork(composer, segment, out WorkEntry whole))
            {
                return whole.Canonical;
            }

            string head = segment.Split('-')[0].Trim();

            if (head.Length > 0
                && !string.Equals(head, segment, StringComparison.Ordinal)
                && context.Dictionary.TryResolveWork(composer, head, out WorkEntry fromHead))
            {
                return fromHead.Canonical;
            }
        }

        return null;
    }

    /// <summary>
    /// 単位内の全ファイルを保留として返す。
    /// </summary>
    private static IEnumerable<TagChange> Hold(AlbumUnit unit, HoldReason reason, string rationale)
    {
        return
        [
            .. unit.Tracks.Select(track => new TagChange(
                track.RelativePath,
                TagField.Album,
                track.GetValues(TagField.Album),
                [],
                RULE_ID,
                rationale,
                Severity.Warning,
                reason)),
        ];
    }

    /// <summary>
    /// 値が 1 つに定まっていればそれを返す。定まらなければ null。
    /// </summary>
    private static string? Single(IReadOnlyList<string> values)
    {
        return values.Count == 1 ? values[0] : null;
    }
}

/// <summary>
/// R-501: 同一アルバム名に複数の作曲家／演奏者が混在。
///
/// <c>Symphony No.5</c> のような汎用的な名前に、作曲家も演奏者も異なる複数の録音が
/// 同居している。**自動修正しない。**
///
/// 書式は <c>{作曲家}: {作品名} - {date}/{artist}</c> で確定している
/// （docs/TAGGING_POLICY.md 3.5）。それでも修正案を出せないのは、<c>{作品名}</c> の唯一の供給元である
/// 正規化辞書の作品エントリが未整備だからである（docs/SPEC.md 13章 D6）。<c>{作品名}</c> は作品そのものの
/// 名前であり、<c>title</c>（楽章名）からも <c>album</c>（汎用名）からも一意には取れない。
/// </summary>
public sealed class AlbumNameCollisionRule : IInspectionRule
{
    /// <summary>ルール ID。検査結果からの導線（docs/SPEC.md 7.3.2）が対象を選ぶのに使う。</summary>
    public const string RULE_ID = "R-501";

    /// <inheritdoc />
    public string Id => RULE_ID;

    /// <inheritdoc />
    public Severity Severity => Severity.Warning;

    /// <inheritdoc />
    public string Description => "同一アルバム名に複数の作曲家／演奏者が混在";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        var byAlbum = context.Tracks
            .Where(track => track.GetValues(TagField.Album).Count == 1)
            .GroupBy(track => track.Album!, StringComparer.Ordinal);

        foreach (var album in byAlbum)
        {
            // 表記揺れを人数に数えない。`Pyotr Il'yich Tchaikovsky` と `Pyotr Ilyich Tchaikovsky` は
            // R-201 が直す同一人物であり、ここで 2 人と数えると混在の程度を誤って伝える。
            string[] composers =
            [
                .. album.Select(track => track.Composer)
                    .Where(composer => !string.IsNullOrEmpty(composer))
                    .Select(composer => context.Dictionary.TryResolveComposer(composer, out string canonical)
                        ? canonical
                        : composer!)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal),
            ];

            if (composers.Length < 2)
            {
                continue;
            }

            foreach (TrackTags track in album)
            {
                yield return new TagChange(
                    track.RelativePath,
                    TagField.Album,
                    track.GetValues(TagField.Album),
                    [],
                    Id,
                    $"同名のアルバムに {composers.Length} 人の作曲家が混在（{string.Join(" / ", composers)}）。"
                    + " 「{作曲家}: {作品名} - {date}/{artist}」形式（TAGGING_POLICY 3.5）への移行が要る",
                    Severity.Warning);
            }
        }
    }
}

/// <summary>
/// R-502: アルバム名が日本語。
///
/// 日本語略称（<c>ベト7</c>、<c>マーラー2</c>）と正式な日本語名（<c>歌劇「ローエングリン」</c>）の
/// 混在が未解消である。**どちらも検出する。**
///
/// 書式は <c>{作曲家}: {作品名} - {date}/{artist}</c> で確定しており（docs/TAGGING_POLICY.md 3.5）、
/// 日本語略称は作品エントリの別名として登録すれば書式への移行過程で解消する。
/// **アルバム名だけを個別に日本語→英語へ直す作業は行わない**（6.1）。修正案を出せないのは
/// <c>{作品名}</c> の供給元である作品エントリが未整備だからである（docs/SPEC.md 13章 D6）。
/// 略称と判別できたものは根拠に作曲家の正規形を出す。
/// </summary>
public sealed class JapaneseAlbumNameRule : IInspectionRule
{
    /// <summary>日本語の文字。</summary>
    private static readonly Regex JAPANESE = new(
        @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]",
        RegexOptions.Compiled);

    /// <summary>末尾の番号。<c>ベト7</c> の <c>7</c>。</summary>
    private static readonly Regex TRAILING_NUMBER = new(@"^(?<name>.+?)\s*(?<number>\d+)$", RegexOptions.Compiled);

    /// <inheritdoc />
    public string Id => "R-502";

    /// <inheritdoc />
    public Severity Severity => Severity.Info;

    /// <inheritdoc />
    public string Description => "アルバム名が日本語";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        foreach (TrackTags track in context.Tracks)
        {
            IReadOnlyList<string> values = track.GetValues(TagField.Album);

            if (values.Count != 1 || !JAPANESE.IsMatch(values[0]))
            {
                continue;
            }

            yield return new TagChange(
                track.RelativePath,
                TagField.Album,
                values,
                [],
                Id,
                BuildRationale(values[0], context),
                Severity.Info);
        }
    }

    /// <summary>
    /// 根拠を組み立てる。略称と判別できたら作曲家の正規形を添える。
    /// </summary>
    private static string BuildRationale(string album, InspectionContext context)
    {
        Match match = TRAILING_NUMBER.Match(album);

        if (match.Success
            && context.Dictionary.TryResolveComposer(match.Groups["name"].Value, out string composer))
        {
            return $"日本語略称。「{composer}」の第 {match.Groups["number"].Value} 番と思われる。"
                + " 「{作曲家}: {作品名} - {date}/{artist}」形式（TAGGING_POLICY 3.5）への移行が要る";
        }

        return "アルバム名が日本語。「{作曲家}: {作品名} - {date}/{artist}」形式（TAGGING_POLICY 3.5）"
            + "への移行が要るが、作品名の供給元（作品エントリ）が未整備のため修正案は出せない";
    }
}

/// <summary>
/// R-503: 楽章番号の書式がフォルダ内で不統一（docs/TAGGING_POLICY.md 6.2）。
///
/// <c>1.</c> / <c>5-1.</c> / <c>1 Allegro</c> / <c>I.</c> / 番号なし が混在している。
/// **統一方針が未決定なので自動修正しない。** どの書式に寄せるかを決められない。
/// </summary>
public sealed class MovementNumberStyleRule : IInspectionRule
{
    /// <summary>書式の判定。上から順に試す。</summary>
    private static readonly (Regex Pattern, string Label)[] STYLES =
    [
        (new Regex(@"^\d+-\d+\.", RegexOptions.Compiled), "5-1. 形式"),
        (new Regex(@"^\d+\.", RegexOptions.Compiled), "1. 形式"),
        (new Regex(@"^\d+\s+\S", RegexOptions.Compiled), "1 Allegro 形式"),
        (new Regex(@"^\(\s*[IVXLC]+[\.\s]", RegexOptions.Compiled), "(I. 形式"),
        (new Regex(@"^[IVXLC]+[\.\s]", RegexOptions.Compiled), "I. 形式"),
        (new Regex(@"^第[０-９0-9一二三四五六七八九十]+楽章", RegexOptions.Compiled), "第N楽章 形式"),
    ];

    /// <summary>番号が無い場合の表示。</summary>
    private const string NO_NUMBER = "番号なし";

    /// <inheritdoc />
    public string Id => "R-503";

    /// <inheritdoc />
    public Severity Severity => Severity.Info;

    /// <inheritdoc />
    public string Description => "楽章番号の書式がフォルダ内で不統一";

    /// <inheritdoc />
    public IEnumerable<TagChange> Inspect(InspectionContext context)
    {
        var byFolder = context.Tracks
            .GroupBy(track => InspectionContext.GetFolder(track.RelativePath), StringComparer.OrdinalIgnoreCase);

        foreach (var folder in byFolder)
        {
            TrackTags[] tracks = [.. folder.Where(track => track.GetValues(TagField.Title).Count == 1)];

            if (tracks.Length < 2)
            {
                continue;
            }

            string[] styles = [.. tracks.Select(track => GetStyle(track.Title!)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

            if (styles.Length < 2)
            {
                continue;
            }

            foreach (TrackTags track in tracks)
            {
                yield return new TagChange(
                    track.RelativePath,
                    TagField.Title,
                    track.GetValues(TagField.Title),
                    [],
                    Id,
                    $"このフォルダに {string.Join(" / ", styles)} が混在（この曲は {GetStyle(track.Title!)}）。"
                    + " 統一方針は未決定（TAGGING_POLICY 6.2）",
                    Severity.Info);
            }
        }
    }

    /// <summary>
    /// 曲名の先頭から楽章番号の書式を判定する。
    /// </summary>
    /// <param name="title">曲名。</param>
    /// <returns>書式の表示名。</returns>
    public static string GetStyle(string title)
    {
        foreach ((Regex pattern, string label) in STYLES)
        {
            if (pattern.IsMatch(title))
            {
                return label;
            }
        }

        return NO_NUMBER;
    }
}
