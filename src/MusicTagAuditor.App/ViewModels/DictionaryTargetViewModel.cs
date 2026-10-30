using System.Windows.Input;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Editing;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// ファイル一覧の値をどの経路で辞書に足すか。
/// </summary>
public enum DictionaryTargetKind
{
    /// <summary>作曲家・人物・団体として足す（「辞書に追加」ダイアログ）。</summary>
    Value,

    /// <summary>作品として足す（「作品を辞書に追加」ダイアログ）。</summary>
    Work,
}

/// <summary>
/// ファイル一覧の右クリック「辞書に追加」に並べる 1 項目（docs/SPEC.md 5.2）。
///
/// **押せない項目も消さずに並べ、理由を添える。** 黙って出さないと、登録済みなのか
/// 導線が無いのかが画面から読めず、結局コピーして辞書タブへ貼りに行くことになる。
/// </summary>
public sealed class DictionaryTargetViewModel
{
    /// <summary>
    /// 項目を作る。
    /// </summary>
    /// <param name="row">右クリックされた行。</param>
    /// <param name="field">値のあるフィールド。</param>
    /// <param name="value">登録する値。</param>
    /// <param name="kind">登録の経路。</param>
    /// <param name="category">作曲家・人物・団体のどれとして足すか。作品では使わない。</param>
    /// <param name="composer">作品の作曲家の正規形。作品以外・決まらないときは null。</param>
    /// <param name="blockedReason">押せない理由。押せるなら空。</param>
    /// <param name="command">押したときに実行するコマンド。引数にはこの項目自身が渡る。</param>
    public DictionaryTargetViewModel(
        TrackRowViewModel row,
        TagField field,
        string value,
        DictionaryTargetKind kind,
        DictionaryCategory category,
        string? composer,
        string blockedReason,
        ICommand? command)
    {
        Row = row;
        Field = field;
        Value = value;
        Kind = kind;
        Category = category;
        Composer = composer;
        BlockedReason = blockedReason;
        Command = command;
    }

    /// <summary>右クリックされた行。</summary>
    public TrackRowViewModel Row { get; }

    /// <summary>値のあるフィールド。</summary>
    public TagField Field { get; }

    /// <summary>登録する値。</summary>
    public string Value { get; }

    /// <summary>登録の経路。</summary>
    public DictionaryTargetKind Kind { get; }

    /// <summary>作曲家・人物・団体のどれとして足すか。</summary>
    public DictionaryCategory Category { get; }

    /// <summary>作品の作曲家の正規形。</summary>
    public string? Composer { get; }

    /// <summary>押せない理由。押せるなら空。</summary>
    public string BlockedReason { get; }

    /// <summary>押せるか。</summary>
    public bool CanAdd => BlockedReason.Length == 0;

    /// <summary>
    /// 押したときに実行するコマンド。
    ///
    /// 項目に持たせるのは、下位メニューの項目から <c>ContextMenu</c> の祖先を辿る
    /// バインドに頼らないため。下位メニューは別の Popup に載り、祖先探索が届くかどうかが
    /// 項目の生成のされ方に左右される。
    /// </summary>
    public ICommand? Command { get; }

    /// <summary>メニューに出す文言。</summary>
    public string Header => CanAdd
        ? $"{ManualEditConst.Label(Field)}: {Value}"
        : $"{ManualEditConst.Label(Field)}: {Value}（追加できません）";

    /// <summary>ツールチップ。押せるなら登録先、押せないなら理由。</summary>
    public string ToolTip => CanAdd
        ? Kind == DictionaryTargetKind.Work
            ? $"「{Composer}」の作品として登録します。"
            : $"{CategoryLabel(Category)}として登録します。既存エントリの別名にもできます。"
        : BlockedReason;

    /// <summary>
    /// 行の値から「辞書に追加」の項目を組み立てる。
    ///
    /// **辞書が扱わないフィールドは並べない**（ジャンル・年・トラック・ディスク・コメント）。
    /// 候補の入力補助（5.2）が候補を出さないフィールドと同じ線引きにしてある。
    /// </summary>
    /// <param name="row">右クリックされた行。</param>
    /// <param name="index">現在の辞書の索引。</param>
    /// <param name="unit">行が属するアルバム単位。検査前なら null。</param>
    /// <param name="command">項目を押したときのコマンド。</param>
    /// <returns>メニューの並び順に並べた項目。</returns>
    public static IReadOnlyList<DictionaryTargetViewModel> Build(
        TrackRowViewModel row,
        DictionaryIndex index,
        AlbumUnit? unit,
        ICommand? command)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(index);

        List<DictionaryTargetViewModel> targets = [];

        // 並びは一覧の列順に合わせる。メニューと列の順序が違うと、目で対応を取り直すことになる。
        string? workComposer = ResolveRowComposer(row, index, out string composerReason);

        foreach (string title in row.GetValuesForDictionary(TagField.Title))
        {
            targets.Add(new DictionaryTargetViewModel(
                row,
                TagField.Title,
                title,
                DictionaryTargetKind.Work,
                DictionaryCategory.Composer,
                workComposer,
                WorkBlockedReason(index, unit, workComposer, composerReason, title),
                command));
        }

        AddValueTargets(targets, row, index, TagField.Composer, DictionaryCategory.Composer, command);
        AddValueTargets(targets, row, index, TagField.Artist, DictionaryCategory.Person, command);
        AddValueTargets(targets, row, index, TagField.Conductor, DictionaryCategory.Person, command);
        AddValueTargets(targets, row, index, TagField.AlbumArtist, DictionaryCategory.Ensemble, command);

        // アルバムは単位の作曲家で引く。「作品を辞書に追加」（7.3.2）と同じく、
        // 単位内に作曲家が複数あれば作品を足しても保留は解けないので押させない。
        string? unitComposer = unit is { Composers.Count: 1 } ? unit.Composers[0] : null;
        string unitReason = unit switch
        {
            null => string.Empty,
            { Composers.Count: 0 } => "このアルバム単位には作曲家が入っていません。先に composer を入れてください。",
            { Composers.Count: > 1 } =>
                "このアルバム単位には作曲家が複数います。作品を足しても保留は解けないため、"
                + "検査結果タブの「このアルバムの扱いを決める」で扱います（TAGGING_POLICY 3.5 規則5・規則6）。",
            _ => string.Empty,
        };

        foreach (string album in row.GetValuesForDictionary(TagField.Album))
        {
            targets.Add(new DictionaryTargetViewModel(
                row,
                TagField.Album,
                album,
                DictionaryTargetKind.Work,
                DictionaryCategory.Composer,
                unitComposer,
                WorkBlockedReason(index, unit, unitComposer, unitReason, album),
                command));
        }

        return targets;
    }

    /// <summary>
    /// 作曲家・人物・団体の項目を足す。
    /// </summary>
    private static void AddValueTargets(
        List<DictionaryTargetViewModel> targets,
        TrackRowViewModel row,
        DictionaryIndex index,
        TagField field,
        DictionaryCategory category,
        ICommand? command)
    {
        foreach (string value in row.GetValuesForDictionary(field))
        {
            string reason = string.Empty;

            if (DictionaryEditor.IsAlreadyKnown(index, value, out string owner))
            {
                reason = $"既に{owner}として辞書にあります。";
            }
            else if (field == TagField.AlbumArtist && index.IsProtectedAlbumArtist(value))
            {
                // 配役情報を含む値は団体名ではない。団体として登録すると、書き換えない約束（2.3）を崩す。
                reason = "配役情報を含むため書き換えない値（protectedAlbumArtists）です。団体としては登録しません（TAGGING_POLICY 2.3）。";
            }

            targets.Add(new DictionaryTargetViewModel(row, field, value, DictionaryTargetKind.Value, category, null, reason, command));
        }
    }

    /// <summary>
    /// 行の <c>composer</c> を作品の作曲家として正規形に寄せる。
    ///
    /// **単位ではなく行の作曲家を使う。** カップリング盤では単位内に作曲家が複数いるが、
    /// 曲名が属する作品の作曲家はその行の <c>composer</c> で決まる。
    /// </summary>
    private static string? ResolveRowComposer(TrackRowViewModel row, DictionaryIndex index, out string reason)
    {
        IReadOnlyList<string> composers = row.GetValuesForDictionary(TagField.Composer);

        if (composers.Count == 0)
        {
            reason = "この行には作曲家が入っていません。作品は作曲家と組で登録するため、先に composer を入れてください。";
            return null;
        }

        if (composers.Count > 1)
        {
            reason = "この行には作曲家が複数入っています。作品はどの作曲家のものかを 1 人に決めて登録します。";
            return null;
        }

        if (!index.TryResolveComposer(composers[0], out string canonical))
        {
            reason = $"作曲家「{composers[0]}」が辞書にありません。先に作曲家を登録してください。";
            return null;
        }

        reason = string.Empty;
        return canonical;
    }

    /// <summary>
    /// 作品の項目を押せない理由を決める。
    /// </summary>
    private static string WorkBlockedReason(
        DictionaryIndex index,
        AlbumUnit? unit,
        string? composer,
        string composerReason,
        string value)
    {
        // 作品のダイアログはアルバム単位（フォルダ + disc）を土台にする。単位は検査で組み立てる。
        if (unit is null)
        {
            return "検査を実行すると登録できます。作品はアルバム単位で扱うため、検査の結果を使います。";
        }

        if (composer is null)
        {
            return composerReason;
        }

        return index.TryResolveWork(composer, value, out WorkEntry work)
            ? $"既に「{work.Composer}」の作品「{work.Canonical}」として辞書にあります。"
            : string.Empty;
    }

    /// <summary>
    /// 種別の表示名。
    /// </summary>
    private static string CategoryLabel(DictionaryCategory category)
    {
        return category switch
        {
            DictionaryCategory.Composer => DictionaryValidator.CATEGORY_COMPOSER,
            DictionaryCategory.Person => DictionaryValidator.CATEGORY_PERSON,
            _ => DictionaryValidator.CATEGORY_ENSEMBLE,
        };
    }
}
