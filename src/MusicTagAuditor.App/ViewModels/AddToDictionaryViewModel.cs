using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicTagAuditor.Core.Dictionary;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// 「検査結果から辞書に追加」ダイアログのビューモデル（docs/SPEC.md 7.3）。
///
/// **この導線が辞書機能で最も使用頻度が高い。** 未知の値を見つけたその場で
/// 「これは〜のエイリアス」と登録し、再検査まで一息で進めるためのもの。
/// </summary>
public sealed partial class AddToDictionaryViewModel : ObservableObject
{
    /// <summary>編集前の辞書。</summary>
    private readonly TagDictionary _dictionary;

    /// <summary>現在の索引。二重登録の確認に使う。</summary>
    private readonly DictionaryIndex _index;

    /// <summary>種別。既定は検出フィールドからの推定値。</summary>
    [ObservableProperty]
    private DictionaryCategory _category;

    /// <summary>既存エントリの別名として足すか。false なら新規エントリを作る。</summary>
    [ObservableProperty]
    private bool _addsToExisting = true;

    /// <summary>既存エントリの絞り込み文字列。</summary>
    [ObservableProperty]
    private string _canonicalFilter = string.Empty;

    /// <summary>選ばれた既存エントリの正規形。</summary>
    [ObservableProperty]
    private string? _selectedCanonical;

    /// <summary>新規エントリの正規形。</summary>
    [ObservableProperty]
    private string _newCanonical;

    /// <summary>新規の団体に付ける実体 ID。</summary>
    [ObservableProperty]
    private string _newEntityId = string.Empty;

    /// <summary>新規の人物が指揮者か。</summary>
    [ObservableProperty]
    private bool _isConductor = true;

    /// <summary>新規の人物がソリストか。</summary>
    [ObservableProperty]
    private bool _isSoloist;

    /// <summary>注意書き。二重登録や原則との齟齬をここに出す。</summary>
    [ObservableProperty]
    private string _notice = string.Empty;

    /// <summary>
    /// 未知の値からダイアログを組み立てる。
    /// </summary>
    /// <param name="dictionary">編集前の辞書。</param>
    /// <param name="index">現在の索引。</param>
    /// <param name="unknown">辞書に無い値。</param>
    public AddToDictionaryViewModel(TagDictionary dictionary, DictionaryIndex index, UnknownValue unknown)
    {
        ArgumentNullException.ThrowIfNull(unknown);

        _dictionary = dictionary;
        _index = index;

        Unknown = unknown;
        _category = unknown.Category;
        _newCanonical = unknown.Value;

        // コンストラクタではプロパティセッターを経由しないため、OnNewCanonicalChanged が
        // 呼ばれず実体 ID の提案が空のままになる。ここで明示的に埋める。
        _newEntityId = DictionaryEditor.SuggestEntityId(_dictionary, _newCanonical);

        RefreshCanonicals();
        UpdateNotice();
    }

    /// <summary>対象の未知の値。</summary>
    public UnknownValue Unknown { get; }

    /// <summary>選べる既存エントリの正規形。</summary>
    public ObservableCollection<string> Canonicals { get; } = [];

    /// <summary>見出しに出す説明。</summary>
    public string Header => string.Create(
        CultureInfo.CurrentCulture,
        $"「{Unknown.Value}」 — {Unknown.Count:N0} ファイル（{Unknown.FieldsText}）");

    /// <summary>代表ファイルのパス。どのアルバムの話かを掴むために出す。</summary>
    public string SamplePath => Unknown.SampleRelativePath;

    /// <summary>
    /// この設定で辞書を更新できるかを判定する。
    /// </summary>
    /// <param name="reason">できない理由。</param>
    /// <returns>更新できるなら true。</returns>
    public bool CanApply(out string reason)
    {
        if (AddsToExisting)
        {
            if (string.IsNullOrWhiteSpace(SelectedCanonical))
            {
                reason = "足す先の正規形を選んでください。";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(NewCanonical))
        {
            reason = "正規形を入力してください。";
            return false;
        }

        if (Category == DictionaryCategory.Ensemble && string.IsNullOrWhiteSpace(NewEntityId))
        {
            reason = "実体 ID を入力してください。団体の同一性は実体 ID で判断します。";
            return false;
        }

        if (Category == DictionaryCategory.Person && !IsConductor && !IsSoloist)
        {
            reason = "役割を 1 つ以上選んでください。";
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
        if (AddsToExisting)
        {
            return DictionaryEditor.AddAlias(_dictionary, Category, SelectedCanonical!, Unknown.Value);
        }

        string canonical = NewCanonical.Trim();

        // 正規形そのものがタグの値と違う場合、タグの値は別名として登録する。
        // 同じなら SplitByScript が落とすので、そのまま渡してよい。
        string[] aliases = [Unknown.Value];

        return Category switch
        {
            DictionaryCategory.Composer => DictionaryEditor.AddComposer(_dictionary, canonical, aliases),
            DictionaryCategory.Person => DictionaryEditor.AddPerson(_dictionary, canonical, BuildRoles(), aliases),
            _ => DictionaryEditor.AddEnsemble(_dictionary, NewEntityId.Trim(), canonical, aliases),
        };
    }

    /// <summary>
    /// 種別が変わったら選べる正規形を入れ替える。
    /// </summary>
    partial void OnCategoryChanged(DictionaryCategory value)
    {
        RefreshCanonicals();
        UpdateNotice();
    }

    /// <summary>
    /// 絞り込みが変わったら候補を入れ替える。
    /// </summary>
    partial void OnCanonicalFilterChanged(string value)
    {
        RefreshCanonicals();
    }

    /// <summary>
    /// 新規の正規形が変わったら実体 ID の候補を作り直す。
    /// </summary>
    partial void OnNewCanonicalChanged(string value)
    {
        NewEntityId = DictionaryEditor.SuggestEntityId(_dictionary, value);
    }

    /// <summary>
    /// 選べる正規形を作り直す。
    /// </summary>
    private void RefreshCanonicals()
    {
        string? previous = SelectedCanonical;

        Canonicals.Clear();

        foreach (string canonical in DictionaryEditor.ListCanonicals(_dictionary, Category)
                     .Where(canonical => CanonicalFilter.Length == 0
                         || canonical.Contains(CanonicalFilter, StringComparison.OrdinalIgnoreCase)))
        {
            Canonicals.Add(canonical);
        }

        SelectedCanonical = previous is not null && Canonicals.Contains(previous) ? previous : null;
    }

    /// <summary>
    /// 注意書きを作り直す。
    /// </summary>
    private void UpdateNotice()
    {
        if (DictionaryEditor.IsAlreadyKnown(_index, Unknown.Value, out string owner))
        {
            Notice = $"この値は既に {owner} として辞書にあります。重ねて登録する必要はありません。";
            return;
        }

        Notice = Category switch
        {
            // docs/TAGGING_POLICY.md 2.1: albumartist は演奏団体名を入れる。
            DictionaryCategory.Ensemble =>
                "albumartist に入る値は演奏団体名です。団体名でない値（プレースホルダ等）は登録せず、"
                + "一覧に残したまま CD 実物の確認に回してください。",
            DictionaryCategory.Composer =>
                "正規形は TAGGING_POLICY 5.1 の表に載っている表記に合わせてください。",
            _ =>
                "人名はフルネーム・生没年なし・大文字強調なしで書きます（TAGGING_POLICY 3.2）。",
        };
    }

    /// <summary>
    /// チェック状態から役割を組み立てる。
    /// </summary>
    private IEnumerable<PersonRole> BuildRoles()
    {
        if (IsConductor)
        {
            yield return PersonRole.Conductor;
        }

        if (IsSoloist)
        {
            yield return PersonRole.Soloist;
        }
    }
}
