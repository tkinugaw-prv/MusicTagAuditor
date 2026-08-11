using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Scanning;
using MusicTagAuditor.TagIo;

namespace AlbumProbe;

/// <summary>
/// docs/TAGGING_POLICY.md 3.5（アルバム名の書式）の根拠になった実測を再現する。
///
/// **読み取りだけを行う。** ライブラリには一切書き込まない。
/// </summary>
public static class Program
{
    /// <summary>
    /// 測定を実行してレポートを書き出す。
    /// </summary>
    /// <param name="args">
    /// [0] ライブラリのルート、[1] レポートの出力先。いずれも省略可。
    /// <c>--import-works &lt;path&gt;</c> を渡した場合は測定せず、作品エントリの取り込みだけを行う。
    /// </param>
    /// <returns>終了コード。</returns>
    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 本体アプリと同じ辞書を読む。測定値が本体の判定と食い違わないようにするため。
        string dictionaryDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Const.DICTIONARY_DIRECTORY_NAME);

        if (args.Length > 0 && string.Equals(args[0], Const.IMPORT_WORKS_OPTION, StringComparison.Ordinal))
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine($"使い方: {Const.IMPORT_WORKS_OPTION} <works.json> [辞書のフォルダ]");
                return 1;
            }

            // 第 3 引数で辞書のフォルダを差し替えられる。試すときに %APPDATA% を汚さないため。
            return WorksImport.Run(args[1], args.Length > 2 ? args[2] : dictionaryDirectory);
        }

        string libraryRoot = args.Length > 0 ? args[0] : Const.DEFAULT_LIBRARY_ROOT;
        string reportPath = args.Length > 1
            ? args[1]
            : Path.Combine(AppContext.BaseDirectory, Const.REPORT_FILE_NAME);

        if (!Directory.Exists(libraryRoot))
        {
            Console.Error.WriteLine($"ライブラリが見つかりません: {libraryRoot}");
            return 1;
        }

        DictionaryIndex dictionary = new(DictionaryLoader.LoadOrCreate(dictionaryDirectory));

        LibraryScanner scanner = new(new TagReader());
        ScanResult scan = await scanner.ScanAsync(libraryRoot).ConfigureAwait(false);

        IReadOnlyList<AlbumUnit> units = AlbumUnit.Build(scan.Tracks, dictionary);

        ReportWriter report = new();
        report.Line($"# アルバム単位の実測（{DateTime.Now:yyyy-MM-dd HH:mm}）");
        report.Line();
        report.Line($"対象: `{libraryRoot}`");
        report.Line($"辞書: `{DictionaryLoader.GetUserDictionaryPath(dictionaryDirectory)}`");
        report.Line($"読み取り {scan.Tracks.Count:N0} 件 / 失敗 {scan.Failures.Count:N0} 件 "
            + $"/ {scan.Elapsed.TotalSeconds:F1} 秒");

        if (scan.Failures.Count > 0)
        {
            report.Line();
            report.Line("読み取りに失敗したファイルがあります。件数は失敗分を除いた値です。");
        }

        Measurements.WriteInventory(report, scan, units);
        Measurements.WriteCoherence(report, units);
        Measurements.WriteCollisionCandidates(report, units);
        Measurements.WritePerformerCollisions(report, units);
        Measurements.WriteComposerMismatch(report, scan, dictionary);

        // 作品エントリの雛形はレポートと同じ場所に出す（docs/SPEC.md 7.4.6）。
        string templatePath = Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(reportPath)) ?? AppContext.BaseDirectory,
            Const.WORKS_TEMPLATE_FILE_NAME);

        int works = await WorksTemplate.WriteAsync(report, units, dictionary, templatePath).ConfigureAwait(false);

        await report.SaveAsync(reportPath).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"レポート: {reportPath}");
        Console.WriteLine($"作品エントリの雛形: {templatePath}（{works} 件）");

        return 0;
    }
}
