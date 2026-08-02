using ATL;

namespace TagIoProbe;

/// <summary>
/// z440.atl.core (ATL.NET) に対する V1〜V7 の実測。
/// 判定方法は <see cref="TagLibSharpProbe"/> と揃える。M4A はバイナリレベルの atom も突き合わせる。
/// </summary>
internal sealed class AtlProbe
{
    /// <summary>レポートに出すライブラリ名。</summary>
    public const string LIBRARY_NAME = "ATL.NET";

    /// <summary>ATL が MP4 の未知 atom を <c>AdditionalFields</c> に載せるときのキー。</summary>
    private const string ADDITIONAL_FIELD_CONDUCTOR = "©con";

    /// <summary>
    /// 全検体に対して検証を実行する。
    /// </summary>
    /// <param name="specimens">検証対象の検体。</param>
    /// <returns>検証結果の一覧。</returns>
    public IReadOnlyList<CheckResult> Run(IReadOnlyList<Specimen> specimens)
    {
        List<CheckResult> results = [];

        foreach (Specimen specimen in specimens)
        {
            results.AddRange(RunForSpecimen(specimen));
        }

        return results;
    }

    /// <summary>
    /// 検体 1 件に対して V1〜V7 を実行する。
    /// </summary>
    private IEnumerable<CheckResult> RunForSpecimen(Specimen specimen)
    {
        List<CheckResult> results = [];
        bool isM4a = specimen.Format.StartsWith("M4A", StringComparison.Ordinal);

        IReadOnlyList<AtomInfo> before = isM4a ? Mp4AtomDumper.Dump(specimen.WorkPath) : [];

        string? readBack;
        string[] albumArtistParts;
        try
        {
            Track track = new(specimen.WorkPath);
            track.Conductor = Const.PROBE_CONDUCTOR;
            track.AlbumArtist = Const.PROBE_SEMICOLON_VALUE;
            track.Save();

            Track reopened = new(specimen.WorkPath);
            readBack = reopened.Conductor;
            albumArtistParts = [reopened.AlbumArtist ?? string.Empty];
        }
        catch (Exception ex)
        {
            results.Add(new CheckResult("V1", LIBRARY_NAME, specimen.Format, Verdict.ERROR, Describe(ex)));
            results.Add(new CheckResult("V7", LIBRARY_NAME, specimen.Format, Verdict.ERROR, Describe(ex)));
            return results;
        }

        IReadOnlyList<AtomInfo> after = isM4a ? Mp4AtomDumper.Dump(specimen.WorkPath) : [];

        results.Add(BuildConductorResult(specimen, readBack, after, isM4a));
        results.Add(BuildSemicolonResult(specimen, albumArtistParts, after, isM4a));

        if (isM4a)
        {
            results.Add(BuildFreeformResult(specimen, before, after));
            results.Add(BuildCoverArtResult(specimen, before, after));
            results.Add(BuildDirectAtomResult(specimen));
        }

        return results;
    }

    /// <summary>
    /// V1 / V5 / V6: 指揮者を書き戻せたか、および M4A では ©con に入ったかを判定する。
    /// </summary>
    private static CheckResult BuildConductorResult(
        Specimen specimen,
        string? readBack,
        IReadOnlyList<AtomInfo> after,
        bool isM4a)
    {
        string id = specimen.Format switch
        {
            var f when f.StartsWith("M4A", StringComparison.Ordinal) => "V1",
            "AIFF" => "V6",
            _ => "V5",
        };

        bool roundTripped = readBack == Const.PROBE_CONDUCTOR;

        if (!isM4a)
        {
            IReadOnlyList<string> stored = NeutralReader.ReadRawField(specimen.WorkPath, "CONDUCTOR");
            return new CheckResult(
                id,
                LIBRARY_NAME,
                specimen.Format,
                roundTripped ? Verdict.OK : Verdict.NG,
                $"書き戻し値 = \"{readBack ?? "(null)"}\" / 実フィールド = [{string.Join(" ⟂ ", stored)}]");
        }

        // 「©con が存在するか」ではなく「書いた値がどの atom に入ったか」で判定する。
        AtomInfo? holder = after.FirstOrDefault(atom => atom.Values.Contains(Const.PROBE_CONDUCTOR));

        if (holder is null)
        {
            string atomList = string.Join(", ", after.Select(atom => $"{LastSegment(atom.Path)}({atom.NameHex})"));
            return new CheckResult(
                id,
                LIBRARY_NAME,
                specimen.Format,
                Verdict.NG,
                $"書いた値がどの atom にも見つからない。ilst 配下 = [{atomList}]");
        }

        bool isConductorAtom = holder.NameHex.Equals(
            Convert.ToHexString(Const.ATOM_CONDUCTOR),
            StringComparison.OrdinalIgnoreCase);

        return new CheckResult(
            id,
            LIBRARY_NAME,
            specimen.Format,
            isConductorAtom ? Verdict.OK : Verdict.NG,
            isConductorAtom
                ? "©con (A9636F6E) に書かれた"
                : $"©con ではなく {LastSegment(holder.Path)} ({holder.NameHex}) に書かれた。"
                  + "AIMP は ©con しか読まないためこのままでは見えない");
    }

    /// <summary>
    /// V7: <c>;</c> を含む値が分割されずに保存されるかを判定する。
    /// ATL の AlbumArtist は単一文字列なので、M4A ではバイナリ側の aART の値も確認する。
    /// </summary>
    private static CheckResult BuildSemicolonResult(
        Specimen specimen,
        string[] albumArtistParts,
        IReadOnlyList<AtomInfo> after,
        bool isM4a)
    {
        IReadOnlyList<string> stored = isM4a
            ? [.. after.Where(atom => LastSegment(atom.Path).StartsWith("aART", StringComparison.Ordinal)).SelectMany(atom => atom.Values)]
            : NeutralReader.ReadRawField(specimen.WorkPath, "ALBUMARTIST");

        bool preserved = stored.Count == 1 && stored[0] == Const.PROBE_SEMICOLON_VALUE;

        return new CheckResult(
            "V7",
            LIBRARY_NAME,
            specimen.Format,
            preserved ? Verdict.OK : Verdict.NG,
            $"格納値 {stored.Count} 件 = [{string.Join(" ⟂ ", stored)}] / "
            + $"ライブラリの読み戻し = [{string.Join(" ⟂ ", albumArtistParts)}]");
    }

    /// <summary>
    /// V3: フリーフォーム atom が保存後も残っているかを判定する。
    /// </summary>
    private static CheckResult BuildFreeformResult(
        Specimen specimen,
        IReadOnlyList<AtomInfo> before,
        IReadOnlyList<AtomInfo> after)
    {
        string[] beforeFreeform = [.. before.Where(IsFreeform).Select(atom => atom.Path).Order(StringComparer.Ordinal)];
        string[] afterFreeform = [.. after.Where(IsFreeform).Select(atom => atom.Path).Order(StringComparer.Ordinal)];

        if (beforeFreeform.Length == 0)
        {
            return new CheckResult("V3", LIBRARY_NAME, specimen.Format, Verdict.NOT_APPLICABLE, "この検体に元からフリーフォーム atom が無い");
        }

        string[] lost = [.. beforeFreeform.Except(afterFreeform, StringComparer.Ordinal)];

        return new CheckResult(
            "V3",
            LIBRARY_NAME,
            specimen.Format,
            lost.Length == 0 ? Verdict.OK : Verdict.NG,
            lost.Length == 0
                ? $"保存前後で {beforeFreeform.Length} 件すべて保持: {string.Join(", ", beforeFreeform)}"
                : $"消失 {lost.Length} 件: {string.Join(", ", lost)}");
    }

    /// <summary>
    /// V4: カバーアートが保存後も残っているかを判定する。
    /// </summary>
    private static CheckResult BuildCoverArtResult(
        Specimen specimen,
        IReadOnlyList<AtomInfo> before,
        IReadOnlyList<AtomInfo> after)
    {
        bool hadCover = before.Any(IsCoverArt);
        bool hasCover = after.Any(IsCoverArt);

        if (!hadCover)
        {
            return new CheckResult("V4", LIBRARY_NAME, specimen.Format, Verdict.NOT_APPLICABLE, "この検体に元から covr が無い");
        }

        return new CheckResult(
            "V4",
            LIBRARY_NAME,
            specimen.Format,
            hasCover ? Verdict.OK : Verdict.NG,
            hasCover ? "covr は保持された" : "covr が消失した");
    }

    /// <summary>
    /// V2: AdditionalFields 経由で ©con を直接読み書きできるかを判定する。
    /// </summary>
    private static CheckResult BuildDirectAtomResult(Specimen specimen)
    {
        const string DIRECT_VALUE = "Hans Knappertsbusch";

        try
        {
            Track track = new(specimen.WorkPath);
            track.AdditionalFields[ADDITIONAL_FIELD_CONDUCTOR] = DIRECT_VALUE;
            track.Save();

            IReadOnlyList<AtomInfo> atoms = Mp4AtomDumper.Dump(specimen.WorkPath);
            AtomInfo? written = FindAtom(atoms, Const.ATOM_CONDUCTOR);
            string keys = string.Join(", ", new Track(specimen.WorkPath).AdditionalFields.Keys);

            if (written is null || !written.Values.Contains(DIRECT_VALUE))
            {
                return new CheckResult(
                    "V2",
                    LIBRARY_NAME,
                    specimen.Format,
                    Verdict.NG,
                    $"AdditionalFields[\"{ADDITIONAL_FIELD_CONDUCTOR}\"] への書き込みが反映されない"
                    + $"（©con の値は \"{written?.TextPreview ?? "(なし)"}\" のまま）。"
                    + $"読み取れる追加フィールド = [{keys}]");
            }

            return new CheckResult(
                "V2",
                LIBRARY_NAME,
                specimen.Format,
                Verdict.OK,
                $"AdditionalFields 経由で ©con に書けた。値 = \"{written.TextPreview}\"");
        }
        catch (Exception ex)
        {
            return new CheckResult("V2", LIBRARY_NAME, specimen.Format, Verdict.ERROR, Describe(ex));
        }
    }

    /// <summary>指定した 4 バイトの atom を探す。</summary>
    private static AtomInfo? FindAtom(IReadOnlyList<AtomInfo> atoms, byte[] nameBytes)
    {
        string hex = Convert.ToHexString(nameBytes);
        return atoms.FirstOrDefault(atom => atom.NameHex.Equals(hex, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>フリーフォーム atom かどうかを判定する。</summary>
    private static bool IsFreeform(AtomInfo atom)
    {
        return atom.Path.Contains($"/{Const.ATOM_FREEFORM}", StringComparison.Ordinal);
    }

    /// <summary>カバーアート atom かどうかを判定する。</summary>
    private static bool IsCoverArt(AtomInfo atom)
    {
        return LastSegment(atom.Path).StartsWith(Const.ATOM_COVER_ART, StringComparison.Ordinal);
    }

    /// <summary>パスの末尾要素（atom 名）を取り出す。</summary>
    private static string LastSegment(string path)
    {
        int index = path.LastIndexOf('/');
        return index < 0 ? path : path[(index + 1)..];
    }

    /// <summary>例外を 1 行の説明にする。</summary>
    private static string Describe(Exception ex)
    {
        return $"{ex.GetType().Name}: {ex.Message}";
    }
}
