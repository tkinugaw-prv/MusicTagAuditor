namespace TagIoProbe;

/// <summary>
/// TagLibSharp (TagLib#) に対する V1〜V7 の実測。
/// 判定はライブラリ自身の読み戻しだけに頼らず、M4A については <see cref="Mp4AtomDumper"/> で
/// バイナリレベルの atom も突き合わせる。
/// </summary>
internal sealed class TagLibSharpProbe
{
    /// <summary>レポートに出すライブラリ名。</summary>
    public const string LIBRARY_NAME = "TagLibSharp";

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

        // --- 指揮者の書き込み（V1 / V5 / V6 共通の操作） ---
        string? readBack;
        string[] albumArtists;
        try
        {
            using (TagLib.File file = TagLib.File.Create(specimen.WorkPath))
            {
                file.Tag.Conductor = Const.PROBE_CONDUCTOR;
                file.Tag.AlbumArtists = [Const.PROBE_SEMICOLON_VALUE];
                file.Save();
            }

            using (TagLib.File reopened = TagLib.File.Create(specimen.WorkPath))
            {
                readBack = reopened.Tag.Conductor;
                albumArtists = reopened.Tag.AlbumArtists;
            }
        }
        catch (Exception ex)
        {
            results.Add(new CheckResult("V1", LIBRARY_NAME, specimen.Format, Verdict.ERROR, Describe(ex)));
            results.Add(new CheckResult("V7", LIBRARY_NAME, specimen.Format, Verdict.ERROR, Describe(ex)));
            return results;
        }

        IReadOnlyList<AtomInfo> after = isM4a ? Mp4AtomDumper.Dump(specimen.WorkPath) : [];

        results.Add(BuildConductorResult(specimen, readBack, after, isM4a));
        results.Add(BuildSemicolonResult(specimen, albumArtists, after, isM4a));

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
        // 検体には AIMP が書いた ©con が最初から存在するため、存在確認では判定にならない。
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
                ? $"©con (A9636F6E) に書かれた"
                : $"©con ではなく {LastSegment(holder.Path)} ({holder.NameHex}) に書かれた。"
                  + $"AIMP は ©con しか読まないためこのままでは見えない");
    }

    /// <summary>
    /// V7: <c>;</c> を含む値が複数値に分割されずに保存されるかを判定する。
    /// ライブラリの読み戻しだけでなく、ファイルに実際に格納された値でも確認する。
    /// </summary>
    private static CheckResult BuildSemicolonResult(
        Specimen specimen,
        string[] albumArtists,
        IReadOnlyList<AtomInfo> after,
        bool isM4a)
    {
        IReadOnlyList<string> stored = isM4a
            ? [.. after.Where(atom => LastSegment(atom.Path).StartsWith("aART", StringComparison.Ordinal)).SelectMany(atom => atom.Values)]
            : NeutralReader.ReadRawField(specimen.WorkPath, "ALBUMARTIST");

        bool preserved = stored.Count == 1 && stored[0] == Const.PROBE_SEMICOLON_VALUE;

        // M4A では、汎用の Tag.AlbumArtists と AppleTag.GetText で挙動が異なるかを併せて記録する。
        // 実装時にどちらの経路を使うべきかの判断材料になる。
        string appleTagNote = string.Empty;
        if (isM4a)
        {
            try
            {
                using TagLib.File file = TagLib.File.Create(specimen.WorkPath);
                string[] viaAppleTag = file.GetTag(TagLib.TagTypes.Apple) is TagLib.Mpeg4.AppleTag tag
                    ? tag.GetText(new TagLib.ReadOnlyByteVector("aART"))
                    : [];
                appleTagNote = $" / AppleTag.GetText {viaAppleTag.Length} 件 = [{string.Join(" ⟂ ", viaAppleTag)}]";
            }
            catch (Exception ex)
            {
                appleTagNote = $" / AppleTag.GetText 失敗: {Describe(ex)}";
            }
        }

        return new CheckResult(
            "V7",
            LIBRARY_NAME,
            specimen.Format,
            preserved ? Verdict.OK : Verdict.NG,
            $"格納値 {stored.Count} 件 = [{string.Join(" ⟂ ", stored)}] / "
            + $"Tag.AlbumArtists {albumArtists.Length} 件 = [{string.Join(" ⟂ ", albumArtists)}]"
            + appleTagNote);
    }

    /// <summary>
    /// V3: フリーフォーム atom（<c>----:com.apple.iTunes:*</c>）が保存後も残っているかを判定する。
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
            return new CheckResult(
                "V3",
                LIBRARY_NAME,
                specimen.Format,
                Verdict.NOT_APPLICABLE,
                "この検体に元からフリーフォーム atom が無い");
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
    /// V4: カバーアート（<c>covr</c>）が保存後も残っているかを判定する。
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
    /// V2: 未知の 4 文字 atom（ここでは ©con）を直接読み書きできるかを判定する。
    /// V1 が NG だった場合の回避策（docs/SPEC.md 4.2 案 C）が成立するかの確認。
    /// </summary>
    private static CheckResult BuildDirectAtomResult(Specimen specimen)
    {
        const string DIRECT_VALUE = "Sergiu Celibidache";

        try
        {
            using (TagLib.File file = TagLib.File.Create(specimen.WorkPath))
            {
                if (file.GetTag(TagLib.TagTypes.Apple, create: true) is not TagLib.Mpeg4.AppleTag appleTag)
                {
                    return new CheckResult("V2", LIBRARY_NAME, specimen.Format, Verdict.NG, "AppleTag を取得できない");
                }

                appleTag.SetText(new TagLib.ReadOnlyByteVector(Const.ATOM_CONDUCTOR), DIRECT_VALUE);
                file.Save();
            }

            IReadOnlyList<AtomInfo> atoms = Mp4AtomDumper.Dump(specimen.WorkPath);
            AtomInfo? written = atoms.FirstOrDefault(
                atom => atom.NameHex.Equals(Convert.ToHexString(Const.ATOM_CONDUCTOR), StringComparison.OrdinalIgnoreCase));

            if (written is null)
            {
                return new CheckResult("V2", LIBRARY_NAME, specimen.Format, Verdict.NG, "SetText 後も ©con が見つからない");
            }

            // 書けるだけでなく、同じ経路で読み戻せることも確認する。
            string[] readBack;
            using (TagLib.File reopened = TagLib.File.Create(specimen.WorkPath))
            {
                readBack = reopened.GetTag(TagLib.TagTypes.Apple) is TagLib.Mpeg4.AppleTag tag
                    ? tag.GetText(new TagLib.ReadOnlyByteVector(Const.ATOM_CONDUCTOR))
                    : [];
            }

            bool writeOk = written.Values.Contains(DIRECT_VALUE);
            bool readOk = readBack.Length == 1 && readBack[0] == DIRECT_VALUE;

            return new CheckResult(
                "V2",
                LIBRARY_NAME,
                specimen.Format,
                writeOk && readOk ? Verdict.OK : Verdict.NG,
                $"書き込み: ©con = \"{written.TextPreview}\" / "
                + $"GetText による読み戻し {readBack.Length} 件 = [{string.Join(" ⟂ ", readBack)}]");
        }
        catch (Exception ex)
        {
            return new CheckResult("V2", LIBRARY_NAME, specimen.Format, Verdict.ERROR, Describe(ex));
        }
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
