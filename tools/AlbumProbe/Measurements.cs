using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;

namespace AlbumProbe;

/// <summary>
/// docs/TAGGING_POLICY.md 3.5 補足2 の実測を再現する。
///
/// **同じ測定を繰り返せることが目的。** 3.5 の書式は測定値を根拠に決めており、
/// composer を直したり辞書を育てたりすると数字が動く。数字が動いたときに
/// 判断が変わるかを確かめられるよう、測定手順そのものを残す。
/// </summary>
public static class Measurements
{
    /// <summary>
    /// アルバム単位の台帳を出す。複数ディスクのフォルダは 3.5 規則3 の対象。
    /// </summary>
    /// <param name="report">出力先。</param>
    /// <param name="scan">走査結果。</param>
    /// <param name="units">アルバム単位。</param>
    public static void WriteInventory(ReportWriter report, ScanResult scan, IReadOnlyList<AlbumUnit> units)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(units);

        IGrouping<string, AlbumUnit>[] byFolder =
            [.. units.GroupBy(unit => unit.Folder, StringComparer.OrdinalIgnoreCase)];
        IGrouping<string, AlbumUnit>[] multiDisc = [.. byFolder.Where(folder => folder.Count() > 1)];

        report.Heading(2, "1. アルバム単位の台帳");
        report.TableHeader("項目", "値");
        report.TableRow("ファイル", $"{scan.Tracks.Count:N0}");
        report.TableRow("フォルダ", $"{byFolder.Length:N0}");
        report.TableRow("アルバム単位（フォルダ + disc）", $"{units.Count:N0}");
        report.TableRow("複数ディスクのフォルダ", $"{multiDisc.Length:N0}");
        report.TableRow("複数ディスクフォルダ配下の単位", $"{multiDisc.Sum(folder => folder.Count()):N0}");
        report.TableRow(
            "複数ディスクフォルダ配下のファイル",
            $"{multiDisc.Sum(folder => folder.Sum(unit => unit.Tracks.Count)):N0}");

        report.Heading(3, "1.1 複数ディスクのフォルダ（3.5 規則3 の対象）");
        report.TableHeader("フォルダ", "ディスク", "ファイル");

        foreach (IGrouping<string, AlbumUnit> folder in multiDisc.OrderBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
        {
            string discs = string.Join(", ", folder.OrderBy(unit => unit.Disc).Select(unit => unit.Disc));
            report.TableRow($"`{folder.Key}`", $"{folder.Count()} 枚 ({discs})", $"{folder.Sum(u => u.Tracks.Count)}");
        }
    }

    /// <summary>
    /// 単位内でフィールドが一意に決まるかを数える。
    /// アルバム名を 1 つに決めるには composer / date / artist がそれぞれ 1 値に定まる必要がある。
    /// </summary>
    /// <param name="report">出力先。</param>
    /// <param name="units">アルバム単位。</param>
    public static void WriteCoherence(ReportWriter report, IReadOnlyList<AlbumUnit> units)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(units);

        report.Heading(2, "2. 単位内でフィールドが一意に決まるか");
        report.TableHeader("フィールド", "0 値（未設定）", "1 値（決まる）", "2 値以上（決まらない）");
        WriteCoherenceRow(report, "composer", units, unit => unit.Composers);
        WriteCoherenceRow(report, "date", units, unit => unit.Dates);
        WriteCoherenceRow(report, "artist", units, unit => unit.Artists);
        WriteCoherenceRow(report, "albumartist", units, unit => unit.AlbumArtists);
        WriteCoherenceRow(report, "album（現在値）", units, unit => unit.Albums);

        AlbumUnit[] multiComposer = [.. units.Where(unit => unit.Composers.Count >= 2)];
        report.Heading(3, $"2.1 composer が複数の単位（3.5 規則5・規則6 の対象） — "
            + $"{multiComposer.Length} 単位 / {multiComposer.Sum(u => u.Tracks.Count)} ファイル");
        WriteUnitTable(report, multiComposer, unit => string.Join(" / ", unit.Composers));

        AlbumUnit[] splitDate = [.. units.Where(unit => unit.Dates.Count >= 2)];
        report.Heading(3, $"2.2 date が単位内で割れている — {splitDate.Length} 単位");
        WriteUnitTable(report, splitDate, unit => string.Join(" / ", unit.Dates));

        AlbumUnit[] splitArtist = [.. units.Where(unit => unit.Artists.Count >= 2)];
        report.Heading(3, $"2.3 artist が単位内で割れている — {splitArtist.Length} 単位");
        WriteUnitTable(report, splitArtist, unit => string.Join(" / ", unit.Artists));

        AlbumUnit[] noDate = [.. units.Where(unit => unit.Dates.Count == 0)];
        report.Heading(3, $"2.4 date 未設定 = 保留（3.5 規則2） — "
            + $"{noDate.Length} 単位 / {noDate.Sum(u => u.Tracks.Count)} ファイル");
        WriteUnitTable(
            report,
            noDate,
            unit => $"{AlbumUnit.Single(unit.Composers)} / {AlbumUnit.Single(unit.Artists)}");
    }

    /// <summary>
    /// アルバム名が衝突する候補を出す。
    ///
    /// **作品名を <c>album</c> タグで代用しない。** <c>album</c> 自体が誤っているファイルがあり、
    /// 代用すると作品の同定を誤る（docs/TAGGING_POLICY.md 3.5 補足2）。
    /// 代わりに作曲家・演奏者・年の 3 要素が一致する組をすべて出し、
    /// 作品まで同じかどうかは人間が判定する。
    /// </summary>
    /// <param name="report">出力先。</param>
    /// <param name="units">アルバム単位。</param>
    public static void WriteCollisionCandidates(ReportWriter report, IReadOnlyList<AlbumUnit> units)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(units);

        IGrouping<string, AlbumUnit>[] groups =
        [
            .. units
                .Where(unit => unit.Composers.Count == 1 && unit.Artists.Count == 1 && unit.Dates.Count == 1)
                .GroupBy(
                    unit => string.Join(
                        Const.KEY_SEPARATOR,
                        unit.Composers[0],
                        unit.Artists[0],
                        unit.Dates[0]),
                    StringComparer.Ordinal)
                .Where(group => group.Count() >= 2)
                .OrderByDescending(group => group.Count()),
        ];

        report.Heading(2, $"3. アルバム名の衝突候補 — {groups.Length} 組");
        report.Line("**作曲家・演奏者・年が一致する組。この中で作品名まで同じものだけが本当に衝突する。**");
        report.Line("同一アルバムがディスクやフォルダで割れている組は、同じ名前が付くのが正しい（3.5 規則3）。");
        report.Line();
        report.TableHeader("composer", "artist", "date", "単位");

        foreach (IGrouping<string, AlbumUnit> group in groups)
        {
            string[] key = group.Key.Split(Const.KEY_SEPARATOR);
            string members = string.Join(
                " ; ",
                group.Select(unit => $"`{unit.Folder}` d{unit.Disc}({unit.Tracks.Count})"));

            report.TableRow(key[0], key[1], key[2], members);
        }

        // 演奏者と年を落とした場合に何が融合するか（3.5 補足1 の根拠）。
        // ここだけは作品名の代用として album タグを使う。粒度の比較が目的で、同定の精度は要らない。
        int merged = units
            .Where(unit => unit.Composers.Count == 1 && unit.Albums.Count == 1)
            .GroupBy(
                unit => string.Join(Const.KEY_SEPARATOR, unit.Composers[0], unit.Albums[0]),
                StringComparer.Ordinal)
            .Count(group => group.Count() >= 2);

        report.Line();
        report.Line($"参考: 演奏者と年を落とすと融合するグループは **{merged} 組**"
            + "（作曲家 + 現 `album` 値で束ねた数。3.5 補足1 の根拠）。");
    }

    /// <summary>
    /// 楽団と演奏者の衝突を数える。3.5 補足1 の「なぜ年を先に置くか」の根拠。
    /// </summary>
    /// <param name="report">出力先。</param>
    /// <param name="units">アルバム単位。</param>
    public static void WritePerformerCollisions(ReportWriter report, IReadOnlyList<AlbumUnit> units)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(units);

        AlbumUnit[] resolved = [.. units.Where(unit => unit.AlbumArtists.Count == 1)];

        IGrouping<string, AlbumUnit>[] byEnsemble =
        [
            .. resolved
                .GroupBy(unit => unit.AlbumArtists[0], StringComparer.Ordinal)
                .Where(group => group
                    .Select(unit => AlbumUnit.Single(unit.Artists))
                    .Distinct(StringComparer.Ordinal)
                    .Count() >= 2)
                .OrderByDescending(group => group.Count()),
        ];

        report.Heading(2, $"4. 楽団・演奏者・年の衝突");
        report.Heading(3, $"4.1 同一楽団を複数の演奏者が振っている — {byEnsemble.Length} 団体");
        report.Line("**albumartist はアルバム一覧での識別に使えない**ことを示す（3.5 補足1）。");
        report.Line();
        report.TableHeader("albumartist（実体）", "単位数", "artist");

        foreach (IGrouping<string, AlbumUnit> group in byEnsemble)
        {
            string artists = string.Join(
                " / ",
                group.Select(unit => AlbumUnit.Single(unit.Artists))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal));

            report.TableRow(group.Key, $"{group.Count()}", artists);
        }

        IGrouping<string, AlbumUnit>[] byArtistAndEnsemble =
        [
            .. resolved
                .Where(unit => unit.Artists.Count == 1)
                .GroupBy(
                    unit => string.Join(Const.KEY_SEPARATOR, unit.AlbumArtists[0], unit.Artists[0]),
                    StringComparer.Ordinal)
                .Where(group => group
                    .Select(unit => AlbumUnit.Single(unit.Dates))
                    .Distinct(StringComparer.Ordinal)
                    .Count() >= 2)
                .OrderByDescending(group => group.Count()),
        ];

        report.Heading(3, $"4.2 同一演奏者 × 同一楽団で年が違う — {byArtistAndEnsemble.Length} 組");
        report.Line("**年が唯一の識別子になる組**。3.5 規則1（年を必ず付ける）の根拠。");
        report.Line();
        report.TableHeader("artist", "albumartist（実体）", "単位数", "date");

        foreach (IGrouping<string, AlbumUnit> group in byArtistAndEnsemble)
        {
            string[] key = group.Key.Split(Const.KEY_SEPARATOR);
            string dates = string.Join(
                " / ",
                group.Select(unit => AlbumUnit.Single(unit.Dates))
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal));

            report.TableRow(key[1], key[0], $"{group.Count()}", dates);
        }
    }

    /// <summary>
    /// ファイル名・曲名に <c>composer</c> と違う作曲家名が出てくるファイルを出す（R-210 / 6.9）。
    ///
    /// <c>composer</c> が「辞書の正規形として正しいが、そのファイルの作曲家ではない」状態は
    /// 値が辞書と一致してしまうため R-201 でも R-501 でも検出できない。
    ///
    /// **一致しても誤りとは限らない。** ブラームス『ハイドンの主題による変奏曲』のように、
    /// 曲名が別の作曲家の名前を正当に含む作品がある（docs/TAGGING_POLICY.md 6.9）。
    ///
    /// 判定は本体の検査ルール（R-210）と同じ <see cref="ComposerMismatch"/> を使う。
    /// 実装が割れると、本表と本体の検出件数を突き合わせられなくなる。
    /// </summary>
    /// <param name="report">出力先。</param>
    /// <param name="scan">走査結果。</param>
    /// <param name="dictionary">正規化辞書の索引。</param>
    public static void WriteComposerMismatch(ReportWriter report, ScanResult scan, DictionaryIndex dictionary)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(dictionary);

        List<(string Path, string Tagged, string FromFileName, string FromTitle)> hits = [];

        foreach (TrackTags track in scan.Tracks.OrderBy(t => t.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            ComposerMismatchHit? hit = ComposerMismatch.Find(track, dictionary);

            if (hit is not null)
            {
                hits.Add((
                    track.RelativePath,
                    hit.Tagged,
                    hit.FromFileName ?? string.Empty,
                    hit.FromTitle ?? string.Empty));
            }
        }

        report.Heading(2, $"5. ファイル名・曲名と composer の食い違い（R-210） — "
            + $"{hits.Count} ファイル / {scan.Tracks.Count} ファイル");
        report.Line("**要確認であって誤りの証拠ではない。** 曲名が別の作曲家名を正当に含む作品がある（6.9）。");
        report.Line("**辞書に載っている作曲家名しか検出できない。** 辞書が育つほど誤検出は増える。");
        report.Line("`composer` が未設定のファイルは対象外（R-401 が扱う）。");
        report.Line();
        report.TableHeader("ファイル", "composer タグ", "ファイル名から", "曲名から");

        foreach ((string path, string tagged, string fromFileName, string fromTitle) in hits)
        {
            report.TableRow(
                $"`{path}`",
                tagged,
                fromFileName.Length == 0 ? "-" : fromFileName,
                fromTitle.Length == 0 ? "-" : fromTitle);
        }
    }

    /// <summary>
    /// 単位の数を値の個数別に数えて 1 行書く。
    /// </summary>
    private static void WriteCoherenceRow(
        ReportWriter report,
        string label,
        IReadOnlyList<AlbumUnit> units,
        Func<AlbumUnit, IReadOnlyList<string>> select)
    {
        report.TableRow(
            label,
            $"{units.Count(unit => select(unit).Count == 0)}",
            $"{units.Count(unit => select(unit).Count == 1)}",
            $"{units.Count(unit => select(unit).Count >= 2)}");
    }

    /// <summary>
    /// 単位の一覧を表にする。
    /// </summary>
    private static void WriteUnitTable(ReportWriter report, IReadOnlyList<AlbumUnit> units, Func<AlbumUnit, string> detail)
    {
        if (units.Count == 0)
        {
            report.Line("該当なし。");
            return;
        }

        report.TableHeader("フォルダ", "disc", "ファイル", "値");

        foreach (AlbumUnit unit in units)
        {
            report.TableRow($"`{unit.Folder}`", $"{unit.Disc}", $"{unit.Tracks.Count}", detail(unit));
        }
    }

}
