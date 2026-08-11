using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// 「このアルバムを対象外にする」ダイアログのビューモデル（docs/SPEC.md 7.3.2 / 7.4.5）。
///
/// **フォルダと <c>disc</c> は明細から自動で埋める。** 手で相対パスを打たせない。打ち間違えても
/// 例外が黙って効かなくなるだけで、原因が画面から分からない。実際に、手で書いた綴りが実フォルダと
/// Unicode の正規化形で食い違い、個別例外が効かなかった（2026-08-12）。
///
/// 対象外（3.5 規則6）と作品名の上書き（規則4・規則7）・作曲家の指定（規則5）を**同じダイアログで
/// 選べるようにする**。版の違いや同一演奏の別リリースは、対象外ではなく上書きで扱う。
/// </summary>
public sealed partial class AlbumOverrideViewModel : ObservableObject
{
    /// <summary>編集前の辞書。</summary>
    private readonly TagDictionary _dictionary;

    /// <summary>対象外にするか。false なら作曲家・作品名の指定になる。</summary>
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
    public AlbumOverrideViewModel(TagDictionary dictionary, AlbumUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        _dictionary = dictionary;

        Folder = unit.Folder;
        Disc = unit.Disc;
        UnitComposers = unit.Composers;

        // 単位に居る作曲家を先に出す。主作品 + カップリング（3.5 規則5）で選ぶのはこの中の 1 人。
        foreach (string composer in unit.Composers
                     .Concat(DictionaryEditor.ListCanonicals(dictionary, DictionaryCategory.Composer))
                     .Distinct(StringComparer.Ordinal))
        {
            Composers.Add(composer);
        }

        UpdateNotice();
    }

    /// <summary>対象のフォルダ。ライブラリルートからの相対パス。</summary>
    public string Folder { get; }

    /// <summary>対象のディスク。</summary>
    public int Disc { get; }

    /// <summary>単位内の作曲家。どれを主作品にするかの判断材料として出す。</summary>
    public IReadOnlyList<string> UnitComposers { get; }

    /// <summary>選べる作曲家。単位内のものを先に並べる。</summary>
    public ObservableCollection<string> Composers { get; } = [];

    /// <summary>見出しに出す説明。</summary>
    public string Header => string.Create(
        CultureInfo.CurrentCulture,
        $"{(Folder.Length == 0 ? "(ルート直下)" : Folder)}  /  disc {Disc}");

    /// <summary>単位内の作曲家の一覧。</summary>
    public string ComposersText => UnitComposers.Count == 0
        ? "この単位には composer が入っていません。"
        : $"この単位の composer: {string.Join(" / ", UnitComposers)}";

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

        if (!Excludes && string.IsNullOrWhiteSpace(Composer) && string.IsNullOrWhiteSpace(WorkName))
        {
            reason = "作曲家か作品名のどちらかを指定してください。どちらも空だと何も起きない例外になります。";
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

        Notice = Excludes
            ? "本物のコンピレーション（TAGGING_POLICY 3.5 規則6）に使います。"
                + " 登録して再検査すると、この単位は R-504 の一覧から消えます。"
            : "版の違い（規則4）・同一演奏の別リリース（規則7）は作品名の上書きで、"
                + " 主作品 + カップリング（規則5）は作曲家の指定で扱います。";
    }

    /// <summary>
    /// 空欄を null にする。JSON に空文字を書くと「指定した」ように見える。
    /// </summary>
    private static string? Blank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
