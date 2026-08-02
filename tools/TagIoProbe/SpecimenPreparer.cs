namespace TagIoProbe;

/// <summary>
/// 検証に使う音声ファイル 1 件。
/// </summary>
/// <param name="Format">論理フォーマット名（M4A / FLAC / MP3 / AIFF）。</param>
/// <param name="SourcePath">複製元の実ファイル。読み取り専用で扱う。</param>
/// <param name="WorkPath">複製先。書き込みはこちらにのみ行う。</param>
internal sealed record Specimen(string Format, string SourcePath, string WorkPath);

/// <summary>
/// 実ライブラリから検体を作業フォルダへ複製する。
/// 実ライブラリのファイルは決して書き換えないため、検証は必ず複製に対して行う。
/// </summary>
internal static class SpecimenPreparer
{
    /// <summary>拡張子から論理フォーマット名への対応。</summary>
    private static readonly Dictionary<string, string> FORMAT_BY_EXTENSION = new(StringComparer.OrdinalIgnoreCase)
    {
        [".m4a"] = "M4A",
        [".flac"] = "FLAC",
        [".mp3"] = "MP3",
        [".aif"] = "AIFF",
        [".aiff"] = "AIFF",
    };

    /// <summary>
    /// ライブラリ直下を走査し、フォーマットごとに 1 件ずつ検体を作業フォルダへ複製する。
    /// </summary>
    /// <param name="libraryRoot">実ライブラリのルート。</param>
    /// <param name="workRoot">複製先の作業フォルダ。</param>
    /// <param name="extraSources">フォーマットごとに優先して使いたい実ファイル（省略可）。</param>
    /// <returns>複製した検体の一覧。</returns>
    public static IReadOnlyList<Specimen> Prepare(
        string libraryRoot,
        string workRoot,
        IReadOnlyDictionary<string, string>? extraSources = null)
    {
        Directory.CreateDirectory(workRoot);

        Dictionary<string, string> chosen = new(StringComparer.OrdinalIgnoreCase);

        if (extraSources is not null)
        {
            foreach ((string format, string path) in extraSources)
            {
                if (File.Exists(path))
                {
                    chosen[format] = path;
                }
            }
        }

        foreach (string path in EnumerateAudioFiles(libraryRoot))
        {
            string extension = Path.GetExtension(path);
            if (!FORMAT_BY_EXTENSION.TryGetValue(extension, out string? format))
            {
                continue;
            }

            if (!chosen.ContainsKey(format))
            {
                chosen[format] = path;
            }

            if (chosen.Count == FORMAT_BY_EXTENSION.Values.Distinct().Count())
            {
                break;
            }
        }

        List<Specimen> specimens = [];
        foreach ((string format, string sourcePath) in chosen.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string workPath = Path.Combine(workRoot, $"{format}{Path.GetExtension(sourcePath)}");
            File.Copy(sourcePath, workPath, overwrite: true);
            File.SetAttributes(workPath, FileAttributes.Normal);
            specimens.Add(new Specimen(format, sourcePath, workPath));
        }

        return specimens;
    }

    /// <summary>
    /// 指定した 1 ファイルを検体として作業フォルダへ追加する。
    /// カバーアート付きの検体など、フォーマット既定の 1 件とは別に用意したいものに使う。
    /// </summary>
    /// <param name="format">検体のラベル。M4A 系は "M4A" で始める必要がある。</param>
    /// <param name="sourcePath">複製元の実ファイル。</param>
    /// <param name="workRoot">複製先の作業フォルダ。</param>
    /// <returns>複製した検体。</returns>
    public static Specimen AddExtra(string format, string sourcePath, string workRoot)
    {
        Directory.CreateDirectory(workRoot);
        string workPath = Path.Combine(workRoot, $"{format}{Path.GetExtension(sourcePath)}");
        File.Copy(sourcePath, workPath, overwrite: true);
        File.SetAttributes(workPath, FileAttributes.Normal);
        return new Specimen(format, sourcePath, workPath);
    }

    /// <summary>
    /// カバーアートを持つ M4A を探す。V4 の検証にはカバーアート付きの検体が要る。
    /// </summary>
    /// <param name="libraryRoot">実ライブラリのルート。</param>
    /// <param name="maxScan">走査を打ち切るファイル数。</param>
    /// <returns>見つかったファイルのパス。見つからなければ null。</returns>
    public static string? FindM4aWithCoverArt(string libraryRoot, int maxScan = 300)
    {
        int scanned = 0;
        foreach (string path in EnumerateAudioFiles(libraryRoot))
        {
            if (!Path.GetExtension(path).Equals(".m4a", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (++scanned > maxScan)
            {
                return null;
            }

            try
            {
                using TagLib.File file = TagLib.File.Create(path);
                if (file.Tag.Pictures.Length > 0)
                {
                    return path;
                }
            }
            catch (Exception)
            {
                // 読めないファイルは検体候補から外すだけでよい。
            }
        }

        return null;
    }

    /// <summary>
    /// ライブラリ配下の音声ファイルを列挙する。<c>backup_*</c> フォルダは除外する
    /// （docs/SPEC.md 11章の除外規則。実ライブラリには音源の複製が入った backup フォルダが存在する）。
    /// </summary>
    private static IEnumerable<string> EnumerateAudioFiles(string libraryRoot)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        };

        foreach (string path in Directory.EnumerateFiles(libraryRoot, "*", options))
        {
            string relative = Path.GetRelativePath(libraryRoot, path);
            if (relative.StartsWith("backup_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Const.TARGET_EXTENSIONS.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }
}
