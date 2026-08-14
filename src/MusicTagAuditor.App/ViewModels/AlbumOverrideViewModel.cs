using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// 「このアルバムの扱いを決める」ダイアログのビューモデル（docs/SPEC.md 7.3.2 / 7.4.5）。
///
/// **フォルダと <c>disc</c> は明細から自動で埋める。** 手で相対パスを打たせない。打ち間違えても
/// 例外が黙って効かなくなるだけで、原因が画面から分からない。実際に、手で書いた綴りが実フォルダと
/// Unicode の正規化形で食い違い、個別例外が効かなかった（2026-08-12）。
///
/// 対象外（3.5 規則6）と作品名の上書き（規則4・規則7）・作曲家の指定（規則5）・年の指定（規則2）を
/// **同じダイアログで選べるようにする**。版の違いや同一演奏の別リリースは、対象外ではなく上書きで扱う。
///
/// **年が割れている保留から開いたときは対象外を選ばせない**（<see cref="CanExclude"/>）。
/// そこまで来た単位は作品が決まっている＝主作品が定まっているので、規則6 には当たらない。
/// 選べるままだと、タグが割れた単位を検出から消すだけの操作ができてしまう。
/// </summary>
public sealed partial class AlbumOverrideViewModel : ObservableObject
{
    /// <summary>編集前の辞書。</summary>
    private readonly TagDictionary _dictionary;

    /// <summary>対象外にするか。false なら作曲家・作品名・年の指定になる。</summary>
    [ObservableProperty]
    private bool _excludes = true;

    /// <summary>
    /// そのフォルダの全ディスクに効かせるか。
    ///
    /// 既定は明細から埋めたディスクだけ（docs/SPEC.md 7.3.2）。フォルダ全体にするかは人が決める。
    /// </summary>
    [ObservableProperty]
    private bool _appliesToWholeFolder;

    /// <summary>指定する作曲家。空なら指定しない。</summary>
    [ObservableProperty]
    private string? _composer;

    /// <summary>指定する作品名。空なら指定しない。</summary>
    [ObservableProperty]
    private string _workName = string.Empty;

    /// <summary>指定する年。空なら指定しない。</summary>
    [ObservableProperty]
    private string? _date;

    /// <summary>例外の理由。**空のまま登録させない**（docs/SPEC.md 7.3.2）。</summary>
    [ObservableProperty]
    private string _note = string.Empty;

    /// <summary>注意書き。既存の例外の上書きをここに出す。</summary>
    [ObservableProperty]
    private string _notice = string.Empty;

    /// <summary>
    /// アルバム単位からダイアログを組み立てる。
    /// </summary>
    /// <param name="dictionary">編集前の辞書。</param>
    /// <param name="unit">対象のアルバム単位。フォルダと <c>disc</c> の出どころ。</param>
    /// <param name="hold">開くきっかけになった保留。選べる扱いがこれで変わる。</param>
    public AlbumOverrideViewModel(TagDictionary dictionary, AlbumUnit unit, HoldReason hold = HoldReason.None)
    {
        ArgumentNullException.ThrowIfNull(unit);

        _dictionary = dictionary;

        Folder = unit.Folder;
        Disc = unit.Disc;
        UnitComposers = unit.Composers;
        UnitDates = unit.Dates;

        // 年が割れているだけの単位は、作品が決まっている＝主作品が定まっているので規則6 ではない。
        // 対象外を選ばせると、タグが割れたまま検出から消すだけの操作になる（docs/SPEC.md 7.4.4）。
        CanExclude = hold != HoldReason.DateUnknown;
        Excludes = CanExclude;

        // 単位に居る作曲家を先に出す。主作品 + カップリング（3.5 規則5）で選ぶのはこの中の 1 人。
        foreach (string composer in unit.Composers
                     .Concat(DictionaryEditor.ListCanonicals(dictionary, DictionaryCategory.Composer))
                     .Distinct(StringComparer.Ordinal))
        {
            Composers.Add(composer);
        }

        // **年は単位内にある値からしか選ばせない。** 手で打てると、どのファイルにも入っていない
        // 年を書けてしまう。ここで選ぶのは「主作品の録音年はどれか」であって、年の入力ではない。
        foreach (string date in unit.Dates)
        {
            Dates.Add(date);
        }

        UpdateNotice();
    }

    /// <summary>対象のフォルダ。ライブラリルートからの相対パス。</summary>
    public string Folder { get; }

    /// <summary>対象のディスク。</summary>
    public int Disc { get; }

    /// <summary>単位内の作曲家。どれを主作品にするかの判断材料として出す。</summary>
    public IReadOnlyList<string> UnitComposers { get; }

    /// <summary>単位内の年。割れているときはここに複数入る。</summary>
    public IReadOnlyList<string> UnitDates { get; }

    /// <summary>対象外（3.5 規則6）を選べるか。年の保留から開いたときは選ばせない。</summary>
    public bool CanExclude { get; }

    /// <summary>選べる作曲家。単位内のものを先に並べる。</summary>
    public ObservableCollection<string> Composers { get; } = [];

    /// <summary>選べる年。**単位内にある値だけ。** 実在しない年を書かせない。</summary>
    public ObservableCollection<string> Dates { get; } = [];

    /// <summary>見出しに出す説明。</summary>
    public string Header => string.Create(
        CultureInfo.CurrentCulture,
        $"{(Folder.Length == 0 ? "(ルート直下)" : Folder)}  /  disc {Disc}");

    /// <summary>単位内の作曲家の一覧。</summary>
    public string ComposersText => UnitComposers.Count == 0
        ? "この単位には composer が入っていません。"
        : $"この単位の composer: {string.Join(" / ", UnitComposers)}";

    /// <summary>年が単位内で割れているか。割れていない単位に年の指定は要らない。</summary>
    public bool HasSplitDates => UnitDates.Count > 1;

    /// <summary>単位内の年の一覧。割れているときだけ出す。</summary>
    public string DatesText => HasSplitDates
        ? $"この単位の date: {string.Join(" / ", UnitDates)}（主作品の録音年を選ぶ。3.5 規則2）"
        : string.Empty;

    /// <summary>
    /// この設定で辞書を更新できるかを判定する。
    /// </summary>
    /// <param name="reason">できない理由。</param>
    /// <returns>更新できるなら true。</returns>
    public bool CanApply(out string reason)
    {
        // 理由の書いていない例外は、後から消してよいか判断できない（docs/SPEC.md 7.4.5）。
        if (string.IsNullOrWhiteSpace(Note))
        {
            reason = "理由を入力してください。理由の書いていない例外は、後から消してよいか判断できません。";
            return false;
        }

        if (!Excludes
            && string.IsNullOrWhiteSpace(Composer)
            && string.IsNullOrWhiteSpace(WorkName)
            && string.IsNullOrWhiteSpace(Date))
        {
            reason = "作曲家・作品名・年のいずれかを指定してください。すべて空だと何も起きない例外になります。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// 設定に従って辞書を更新した結果を返す。元の辞書は書き換えない。
    /// </summary>
    /// <returns>更新後の辞書。</returns>
    public TagDictionary Apply()
    {
        return DictionaryEditor.AddAlbumOverride(_dictionary, BuildEntry());
    }

    /// <summary>
    /// 入力から個別例外を組み立てる。
    /// </summary>
    /// <returns>個別例外。</returns>
    public AlbumOverrideEntry BuildEntry()
    {
        return new AlbumOverrideEntry
        {
            Folder = Folder,
            Disc = AppliesToWholeFolder ? null : Disc,
            Composer = Excludes ? null : Blank(Composer),
            WorkName = Excludes ? null : Blank(WorkName),
            Date = Excludes ? null : Blank(Date),
            Exclude = Excludes,
            Note = Blank(Note),
        };
    }

    /// <summary>対象外かどうかで注意書きが変わる。</summary>
    partial void OnExcludesChanged(bool value)
    {
        UpdateNotice();
    }

    /// <summary>適用範囲が変わったら既存の例外を見直す。</summary>
    partial void OnAppliesToWholeFolderChanged(bool value)
    {
        UpdateNotice();
    }

    /// <summary>
    /// 注意書きを作り直す。
    /// </summary>
    private void UpdateNotice()
    {
        int? disc = AppliesToWholeFolder ? null : Disc;

        AlbumOverrideEntry? existing = (_dictionary.AlbumOverrides ?? []).FirstOrDefault(entry =>
            string.Equals(
                DictionaryIndex.NormalizeFolder(entry.Folder),
                DictionaryIndex.NormalizeFolder(Folder),
                StringComparison.OrdinalIgnoreCase)
            && entry.Disc == disc);

        if (existing is not null)
        {
            Notice = $"このフォルダには既に個別例外があります（理由: {existing.Note ?? "(なし)"}）。"
                + " 登録すると、その内容をこの設定で置き換えます。";

            return;
        }

        if (Excludes)
        {
            Notice = "本物のコンピレーション（TAGGING_POLICY 3.5 規則6）に使います。"
                + " 登録して再検査すると、この単位は R-504 の一覧から消えます。";

            return;
        }

        // 対象外を選べない＝年の割れで開いた場合。何をする画面なのかをここで伝える。
        Notice = CanExclude
            ? "版の違い（規則4）・同一演奏の別リリース（規則7）は作品名の上書きで、"
                + " 主作品 + カップリング（規則5）は作曲家の指定で扱います。"
            : "この単位は作品が決まっているので、対象外（規則6）には当たりません。"
                + " 主作品と併録曲で録音年が違うなら、主作品の年を選びます（規則2）。"
                + " 別々の録音がまとまっているだけなら、ここで例外にせずフォルダを分けてください。";
    }

    /// <summary>
    /// 空欄を null にする。JSON に空文字を書くと「指定した」ように見える。
    /// </summary>
    private static string? Blank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
