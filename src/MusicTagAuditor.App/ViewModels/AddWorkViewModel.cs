using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    /// <summary><c>album</c> 由来の候補の出所。</summary>
    public const string SOURCE_ALBUM = "album";

    /// <summary>フォルダ名由来の候補の出所。</summary>
    public const string SOURCE_FOLDER = "フォルダ名";

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
            Candidates.Add(new AliasCandidateViewModel(album, AliasCandidateViewModel.SOURCE_ALBUM));
        }

        foreach (string hint in DictionaryEditor.SuggestWorkAliases(unit.Folder, index))
        {
            Candidates.Add(new AliasCandidateViewModel(hint, AliasCandidateViewModel.SOURCE_FOLDER));
        }

        foreach (WorkNameCandidate candidate in WorkNameSuggester.Suggest(
                     dictionary,
                     index,
                     composer,
                     Candidates.Select(alias => new WorkNameCandidate(alias.Value, alias.Source))))
        {
            NameCandidates.Add(candidate);
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

    /// <summary>
    /// 作品名の候補（docs/SPEC.md 7.3.2）。
    ///
    /// **押したときだけ入力欄に入る。既定値にはしない。** 現在の <c>album</c> の値は誤っていることがあり、
    /// 機械が正規形として採用してはならない（<c>TAGGING_POLICY.md</c> 3.5 補足2）。
    /// </summary>
    public ObservableCollection<WorkNameCandidate> NameCandidates { get; } = [];

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
    /// 候補を作品名の欄に入れる。**入るのは押したときだけ**で、そのあと手で直せる。
    /// </summary>
    /// <param name="candidate">選ばれた候補。</param>
    [RelayCommand]
    private void UseNameCandidate(WorkNameCandidate? candidate)
    {
        if (candidate is not null)
        {
            Canonical = candidate.Value;
        }
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

        // album とフォルダ名が別の番号を指しているなら、どちらかのタグが誤っている（7.4.3 手順5）。
        // 候補を並べるだけでは見落とされる食い違いなので、名指しで出す。
        string[] albumNumbers = NumbersOf(AliasCandidateViewModel.SOURCE_ALBUM);
        string[] folderNumbers = NumbersOf(AliasCandidateViewModel.SOURCE_FOLDER);

        if (albumNumbers.Length > 0 && folderNumbers.Length > 0 && !albumNumbers.Intersect(folderNumbers, StringComparer.Ordinal).Any())
        {
            Notice = $"album は {string.Join(" / ", albumNumbers)} 番、フォルダ名は {string.Join(" / ", folderNumbers)} 番を指しています。"
                + " どちらかのタグが誤っている可能性があります。候補を選ぶ前に、実物がどちらの曲かを確かめてください。";

            return;
        }

        Notice = "作品名は「ジャンル名は英語、固有の題名は原語」で書きます（TAGGING_POLICY 3.5 規則8）。"
            + " 原語が非ラテン文字なら英語圏での一般的な題名を使います（The Nutcracker）。"
            + " 版・稿の違いでエントリを分けないでください（規則4）。";
    }

    /// <summary>
    /// 指定した出所の手がかりに含まれる番号を集める。
    /// </summary>
    private string[] NumbersOf(string source)
    {
        return
        [
            .. Candidates
                .Where(candidate => candidate.Source == source)
                .SelectMany(candidate => WorkNameSuggester.ExtractNumbers(candidate.Value))
                .Distinct(StringComparer.Ordinal),
        ];
    }
}
