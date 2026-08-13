using System.Text.Json;
using MusicTagAuditor.Core.Dictionary;

namespace AlbumProbe;

/// <summary>
/// 作品エントリと個別例外を利用者辞書へ取り込む（docs/SPEC.md 7.4.6）。
///
/// **辞書の読み書きは本体と同じ <see cref="DictionaryLoader"/> / <see cref="DictionaryWriter"/> を使う。**
/// JSON を手で継ぎ足すと、キーの綴りが本体の書き方と食い違って読めない辞書ができる。
/// 実際に手で足した <c>works</c> と本体が書いた <c>Works</c> が衝突した（2026-08-12）。
///
/// 取り込むのは <c>works</c> と <c>albumOverrides</c> だけで、他のセクションには触らない。
/// 保存は <see cref="DictionaryWriter.WriteFile"/> 経由なので直前版が <c>.bak</c> に残る。
/// </summary>
public static class WorksImport
{
    /// <summary>読み込み設定。雛形はコメント（<c>_</c> 始まり）を持つので許す。</summary>
    private static readonly JsonSerializerOptions READ_OPTIONS = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// 取り込みを実行する。
    /// </summary>
    /// <param name="sourcePath">作品エントリを書いた JSON のパス。</param>
    /// <param name="dictionaryDirectory">利用者辞書のフォルダ。</param>
    /// <returns>終了コード。</returns>
    public static int Run(string sourcePath, string dictionaryDirectory)
    {
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine($"取り込む JSON が見つかりません: {sourcePath}");
            return 1;
        }

        TagDictionary? source;

        try
        {
            source = JsonSerializer.Deserialize<TagDictionary>(File.ReadAllText(sourcePath), READ_OPTIONS);
        }
        catch (JsonException exception)
        {
            Console.Error.WriteLine($"JSON を読めません: {exception.Message}");
            return 1;
        }

        // **null を空として扱う。** JSON に `"works": null` と書かれていると、
        // プロパティ初期化子（`= []`）ではなく null が入る。実際に本体が書いた辞書が
        // `"Works": null` を持っていた（2026-08-12）。辞書のほかの読み取り箇所も同じ形で守っている。
        IReadOnlyList<WorkEntry> works = source?.Works ?? [];
        IReadOnlyList<AlbumOverrideEntry> overrides = source?.AlbumOverrides ?? [];

        if (works.Count == 0 && overrides.Count == 0)
        {
            Console.Error.WriteLine("works も albumOverrides も入っていません。");
            return 1;
        }

        // **正規化キーが正規形と同じになる別名を落としてから入れる。**
        // 雛形のエイリアスはフォルダ名そのままなので、`canonical` に `Symphony No. 7` と書くと
        // 雛形が集めた `Symphony No.7` が同じキーで残る。索引は先勝ちで後者を捨てるため
        // 引ける範囲は変わらないが、警告だけが作品の数だけ積み上がる（2026-08-14 に 41 件）。
        IReadOnlyList<RemovedAlias> dropped;
        (works, dropped) = RedundantAliasCleaner.CleanWorks(works);

        DictionaryStore store = new(dictionaryDirectory);

        Console.WriteLine($"辞書: {store.FilePath}");
        Console.WriteLine($"取り込み前: {DictionarySummary.Describe(store.Dictionary)}");

        // 落としたものは黙って捨てず、必ず出す。雛形を書いた本人が「登録したのに一覧に無い」と
        // 迷わないようにするため。
        if (dropped.Count > 0)
        {
            Console.WriteLine($"冗長な別名を {dropped.Count} 件落としました（書かなくても引けます）:");

            foreach (RemovedAlias removed in dropped)
            {
                Console.WriteLine($"  {removed.Summary}");
            }
        }

        TagDictionary merged = store.Dictionary with
        {
            Works = works,
            AlbumOverrides = overrides,
        };

        // **検証で止める。** 自然キーや別名が重複すると索引は先勝ちで、後から書いたほうは
        // 黙って捨てられる。登録したのに効かない状態を作らない（SPEC 7.4.1）。
        IReadOnlyList<DictionaryIssue> issues = DictionaryValidator.Validate(merged);

        foreach (DictionaryIssue issue in issues.Where(issue => issue.Severity == DictionaryIssueSeverity.Error))
        {
            Console.Error.WriteLine($"  {issue.Summary}");
        }

        if (DictionaryValidator.HasError(issues))
        {
            Console.Error.WriteLine("エラーがあるため取り込みませんでした。");
            return 1;
        }

        store.Save(merged);

        int warnings = issues.Count(issue => issue.Severity == DictionaryIssueSeverity.Warning);

        Console.WriteLine(
            $"取り込み後: {DictionarySummary.Describe(merged)}"
            + (warnings > 0 ? $"（警告 {warnings} 件）" : string.Empty));
        Console.WriteLine($"直前版: {store.FilePath}{DictionaryWriter.BACKUP_SUFFIX}");
        Console.WriteLine("アプリを起動し直して、再スキャン → 検査を実行してください。");

        return 0;
    }
}
