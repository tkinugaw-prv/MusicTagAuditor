using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// 別名の候補 1 件（docs/SPEC.md 7.3.2）。
///
/// 候補は**初期値として選択済みで出し、人が取捨選択する**。どこから来た候補かを添えるのは、
/// <c>album</c> の値が誤っていることがあるため（docs/TAGGING_POLICY.md 3.5 補足2）。
/// 実ライブラリには `シューベルト 9` のフォルダに `Schubert Symphony No.8` という
/// <c>album</c> が付いた単位がある。出所が分からないと、この誤りを見抜けない。
/// </summary>
public sealed partial class AliasCandidateViewModel : ObservableObject
{
    /// <summary>登録するか。</summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>
    /// 候補を作る。
    /// </summary>
    /// <param name="value">別名の候補。</param>
    /// <param name="source">出所（<c>album</c> かフォルダ名か）。</param>
    public AliasCandidateViewModel(string value, string source)
    {
        Value = value;
        Source = source;
    }

    /// <summary>別名の候補。</summary>
    public string Value { get; }

    /// <summary>出所。</summary>
    public string Source { get; }

    /// <summary>一覧に出す 1 行。</summary>
    public string DisplayText => $"{Value}    （{Source}）";
}

/// <summary>
/// 「検査結果から作品を辞書に追加」ダイアログのビューモデル（docs/SPEC.md 7.3.2）。
///
/// **これが作品エントリを育てる主経路になる。** 辞書タブで作曲家名を手で打たせる作りにしない。
/// 打ち間違えれば黙って効かないだけで、原因が画面から分からない。
/// </summary>
public sealed partial class AddWorkViewModel : ObservableObject
{
    /// <summary>編集前の辞書。</summary>
    private readonly TagDictionary _dictionary;

    /// <summary>
    /// 作品名（<c>canonical</c>）。
    ///
    /// **空で出し、人が入れる。** 現在の <c>album</c> の値は誤っていることがあり、
    /// 機械が正規形として採用してはならない（docs/TAGGING_POLICY.md 3.5 補足2）。
    /// </summary>
    [ObservableProperty]
    private string _canonical = string.Empty;

    /// <summary>候補に無い別名。1 行 1 件。</summary>
    [ObservableProperty]
    private string _extraAliasesText = string.Empty;

    /// <summary>注意書き。既存の作品との重なりをここに出す。</summary>
    [ObservableProperty]
    private string _notice = string.Empty;

    /// <summary>
    /// アルバム単位からダイアログを組み立てる。
    /// </summary>
    /// <param name="dictionary">編集前の辞書。</param>
    /// <param name="index">現在の索引。作曲家フォルダの判定と既存作品の確認に使う。</param>
    /// <param name="unit">対象のアルバム単位。</param>
    /// <param name="composer">単位の作曲家の正規形。**固定表示にする**（変更させない）。</param>
    public AddWorkViewModel(TagDictionary dictionary, DictionaryIndex index, AlbumUnit unit, string composer)
    {
        ArgumentNullException.ThrowIfNull(unit);

        _dictionary = dictionary;

        Composer = composer;
        Folder = unit.Folder;
        Disc = unit.Disc;

        foreach (string album in unit.Albums)
        {
            Candidates.Add(new AliasCandidateViewModel(album, "album"));
        }

        foreach (string hint in DictionaryEditor.SuggestWorkAliases(unit.Folder, index))
        {
            Candidates.Add(new AliasCandidateViewModel(hint, "フォルダ名"));
        }

        UpdateNotice();
    }

    /// <summary>
    /// この作品の作曲家。**単位から決まっているので選び直させない**（docs/SPEC.md 7.3.2）。
    /// </summary>
    public string Composer { get; }

    /// <summary>対象のフォルダ。</summary>
    public string Folder { get; }

    /// <summary>対象のディスク。</summary>
    public int Disc { get; }

    /// <summary>別名の候補。</summary>
    public ObservableCollection<AliasCandidateViewModel> Candidates { get; } = [];

    /// <summary>見出しに出す説明。</summary>
    public string Header => string.Create(
        CultureInfo.CurrentCulture,
        $"{(Folder.Length == 0 ? "(ルート直下)" : Folder)}  /  disc {Disc}");

    /// <summary>
    /// この設定で辞書を更新できるかを判定する。
    /// </summary>
    /// <param name="reason">できない理由。</param>
    /// <returns>更新できるなら true。</returns>
    public bool CanApply(out string reason)
    {
        if (string.IsNullOrWhiteSpace(Canonical))
        {
            reason = "作品名を入力してください。アルバム名にそのまま入る値です。";
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
        return DictionaryEditor.AddWork(_dictionary, Composer, Canonical.Trim(), BuildAliases());
    }

    /// <summary>
    /// 作品名が変わったら注意書きを出し直す。
    /// </summary>
    partial void OnCanonicalChanged(string value)
    {
        UpdateNotice();
    }

    /// <summary>
    /// 選ばれた候補と手入力の別名をまとめる。
    /// </summary>
    private IEnumerable<string> BuildAliases()
    {
        return Candidates
            .Where(candidate => candidate.IsSelected)
            .Select(candidate => candidate.Value)
            .Concat(AliasText.Split(ExtraAliasesText));
    }

    /// <summary>
    /// 注意書きを作り直す。
    /// </summary>
    private void UpdateNotice()
    {
        string canonical = Canonical.Trim();

        // 自然キーは composer + canonical の組。同じ組があれば新しい行は作らず別名だけを足す。
        if (canonical.Length > 0
            && (_dictionary.Works ?? []).Any(work =>
                string.Equals(work.Composer, Composer, StringComparison.Ordinal)
                && string.Equals(work.Canonical, canonical, StringComparison.Ordinal)))
        {
            Notice = $"「{Composer}」には既に作品「{canonical}」があります。"
                + " 新しい行は作らず、選んだ別名だけをその作品に足します。";

            return;
        }

        Notice = "作品名は「ジャンル名は英語、固有の題名は原語」で書きます（TAGGING_POLICY 3.5 規則8）。"
            + " 原語が非ラテン文字なら英語圏での一般的な題名を使います（The Nutcracker）。"
            + " 版・稿の違いでエントリを分けないでください（規則4）。";
    }
}
