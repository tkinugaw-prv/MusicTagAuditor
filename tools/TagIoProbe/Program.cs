using System.Text;
using TagIoProbe;

// docs/SPEC.md 4章「タグ入出力ライブラリの選定」の検証を実行する使い捨てスパイク。
// 実ライブラリのファイルは読み取りのみ。書き込みは work フォルダへの複製に対してのみ行う。

// 既定値は持たない。特定の環境のパスを埋め込むと他の環境で意味を成さないため。
if (args.Length == 0)
{
    Console.Error.WriteLine("使い方: dotnet run --project tools/TagIoProbe/TagIoProbe.csproj -- <ライブラリのルートパス>");
    return 1;
}

string libraryRoot = args[0];

if (!Directory.Exists(libraryRoot))
{
    Console.Error.WriteLine($"ライブラリが見つかりません: {libraryRoot}");
    return 1;
}

string projectDir = FindProjectDirectory();
string workRoot = Path.Combine(projectDir, Const.WORK_DIR_NAME);

if (Directory.Exists(workRoot))
{
    Directory.Delete(workRoot, recursive: true);
}

// AIMP が ©con を書いた検証済みファイルがあれば M4A の主検体に使う（docs/TAGGING_POLICY.md 4.3）。
Dictionary<string, string> preferredSources = new(StringComparer.OrdinalIgnoreCase);
string? aimpTestFile = FindAimpTestFile(libraryRoot);
if (aimpTestFile is not null)
{
    preferredSources["M4A"] = aimpTestFile;
}

string taglibWork = Path.Combine(workRoot, "taglibsharp");
string atlWork = Path.Combine(workRoot, "atl");

List<Specimen> taglibSpecimens = [.. SpecimenPreparer.Prepare(libraryRoot, taglibWork, preferredSources)];
List<Specimen> atlSpecimens = [.. SpecimenPreparer.Prepare(libraryRoot, atlWork, preferredSources)];

// V4 にはカバーアートを持つ検体が要る。既定の M4A 検体には covr が無いことがあるため別途探す。
string? coverArtSource = SpecimenPreparer.FindM4aWithCoverArt(libraryRoot);
if (coverArtSource is not null)
{
    taglibSpecimens.Add(SpecimenPreparer.AddExtra("M4A-covr", coverArtSource, taglibWork));
    atlSpecimens.Add(SpecimenPreparer.AddExtra("M4A-covr", coverArtSource, atlWork));
}
else
{
    Console.Error.WriteLine("カバーアートを持つ M4A が見つからないため V4 は N/A になります。");
}

StringBuilder report = new();
report.AppendLine("# タグ入出力ライブラリ 実測結果");
report.AppendLine();
report.AppendLine($"実行日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
report.AppendLine($"ライブラリルート: `{libraryRoot}`");
report.AppendLine();

report.AppendLine("## 検体");
report.AppendLine();
report.AppendLine("| フォーマット | 複製元 |");
report.AppendLine("|---|---|");
foreach (Specimen specimen in taglibSpecimens)
{
    report.AppendLine($"| {specimen.Format} | `{Path.GetRelativePath(libraryRoot, specimen.SourcePath)}` |");
}

report.AppendLine();

// --- 書き込み前の atom 一覧（M4A のみ） ---
Specimen? m4a = taglibSpecimens.FirstOrDefault(s => s.Format == "M4A");
if (m4a is not null)
{
    report.AppendLine("## 書き込み前の M4A タグ atom（バイナリ走査）");
    report.AppendLine();
    AppendAtomTable(report, m4a.WorkPath);
}

List<CheckResult> allResults = [];
allResults.AddRange(new TagLibSharpProbe().Run(taglibSpecimens));
allResults.AddRange(new AtlProbe().Run(atlSpecimens));

report.AppendLine("## 検証結果マトリクス");
report.AppendLine();
report.AppendLine("| # | ライブラリ | フォーマット | 判定 | 根拠 |");
report.AppendLine("|---|---|---|---|---|");
foreach (CheckResult result in allResults
             .OrderBy(r => r.Id, StringComparer.Ordinal)
             .ThenBy(r => r.Library, StringComparer.Ordinal))
{
    report.AppendLine($"| {result.Id} | {result.Library} | {result.Format} | {result.Verdict} | {Escape(result.Detail)} |");
}

report.AppendLine();

// --- 書き込み後の atom 一覧（TagLib# / ATL それぞれ） ---
(string Label, List<Specimen> Specimens)[] runs =
[
    (TagLibSharpProbe.LIBRARY_NAME, taglibSpecimens),
    (AtlProbe.LIBRARY_NAME, atlSpecimens),
];

foreach ((string label, List<Specimen> specimens) in runs)
{
    Specimen? target = specimens.FirstOrDefault(s => s.Format == "M4A");
    if (target is null)
    {
        continue;
    }

    report.AppendLine($"## 書き込み後の M4A タグ atom — {label}");
    report.AppendLine();
    AppendAtomTable(report, target.WorkPath);
}

report.AppendLine("## V8: パッケージのバージョンと対応 TFM");
report.AppendLine();
report.AppendLine("| ライブラリ | アセンブリバージョン | 配置場所 |");
report.AppendLine("|---|---|---|");
report.AppendLine(DescribeAssembly(TagLibSharpProbe.LIBRARY_NAME, typeof(TagLib.File)));
report.AppendLine(DescribeAssembly(AtlProbe.LIBRARY_NAME, typeof(ATL.Track)));
report.AppendLine();

report.AppendLine("## AIMP での目視確認（利用者作業）");
report.AppendLine();
report.AppendLine("次のファイルを AIMP で開き、「指揮者」欄に値が表示されるかを確認してください。");
report.AppendLine();
foreach ((string label, List<Specimen> specimens) in runs)
{
    foreach (Specimen specimen in specimens)
    {
        report.AppendLine($"- {label} / {specimen.Format}: `{specimen.WorkPath}`");
    }
}

string reportPath = Path.Combine(workRoot, Const.REPORT_FILE_NAME);
File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine(report.ToString());
Console.WriteLine($"レポートを書き出しました: {reportPath}");

return 0;

// M4A のタグ atom 一覧を Markdown 表としてレポートに追記する。
static void AppendAtomTable(StringBuilder report, string filePath)
{
    report.AppendLine("| atom | hex | 型 | 値 |");
    report.AppendLine("|---|---|---|---|");
    foreach (AtomInfo atom in Mp4AtomDumper.Dump(filePath))
    {
        string typeFlag = atom.DataTypeFlag?.ToString() ?? "-";
        report.AppendLine($"| `{atom.Path}` | {atom.NameHex} | {typeFlag} | {Escape(atom.TextPreview)} |");
    }

    report.AppendLine();
}

// 実行ディレクトリから上に辿って TagIoProbe.csproj のあるフォルダを探す。
static string FindProjectDirectory()
{
    DirectoryInfo? current = new(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "TagIoProbe.csproj")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return AppContext.BaseDirectory;
}

// AIMP で ©con を書き込んだ検証済みファイル（TAGTEST）を backup フォルダから探す。
static string? FindAimpTestFile(string libraryRoot)
{
    EnumerationOptions options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
    };

    return Directory.EnumerateFiles(libraryRoot, "TAGTEST*.m4a", options).FirstOrDefault();
}

// アセンブリのバージョンと配置場所をレポート行にする。
static string DescribeAssembly(string label, Type type)
{
    System.Reflection.Assembly assembly = type.Assembly;
    string version = assembly.GetName().Version?.ToString() ?? "(不明)";
    string location = string.IsNullOrEmpty(assembly.Location) ? "(単一ファイル)" : assembly.Location;
    return $"| {label} | {version} | `{location}` |";
}

// Markdown 表のセルに入れられるようエスケープする。
static string Escape(string? value)
{
    if (value is null)
    {
        return "(null)";
    }

    return value
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);
}
