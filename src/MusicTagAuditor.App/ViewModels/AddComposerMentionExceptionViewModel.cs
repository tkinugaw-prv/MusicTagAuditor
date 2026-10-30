using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// 「この語句を例外に登録」ダイアログのビューモデル（docs/SPEC.md 7.3.2 / docs/TAGGING_POLICY.md 6.9）。
///
/// **作曲家は明細から固定で埋める。** 例外は作曲家と語句の組で効くので、選び直せると別の作曲家の
/// ファイルの検出を黙って止められる。語句は人が決める。どこまでが作品の語句かは機械に分からない。
///
/// **押した行に効くかをその場で試して見せる。** 登録したのに行が消えないと、何度も語句を
/// 打ち直すことになる。
/// </summary>
public sealed partial class AddComposerMentionExceptionViewModel : ObservableObject
{
    /// <summary>編集前の辞書。</summary>
    private readonly TagDictionary _dictionary;

    /// <summary>対象のファイル。効くかを試すのに使う。</summary>
    private readonly TrackTags _track;

    /// <summary>対象と同じフォルダのファイル。</summary>
    private readonly IReadOnlyList<TrackTags> _siblings;

    /// <summary>語句の判定に使う索引。</summary>
    private readonly DictionaryIndex _index;

    /// <summary>照合から外す語句。</summary>
    [ObservableProperty]
    private string _phrase;

    /// <summary>例外の理由。**空のまま登録させない**（docs/SPEC.md 7.3.2）。</summary>
    [ObservableProperty]
    private string _note = string.Empty;

    /// <summary>注意書き。入力した語句がこの行に効くかを出す。</summary>
    [ObservableProperty]
    private string _notice = string.Empty;

    /// <summary>
    /// 明細の行からダイアログを組み立てる。
    /// </summary>
    /// <param name="dictionary">編集前の辞書。</param>
    /// <param name="track">対象のファイル。</param>
    /// <param name="siblings">対象と同じフォルダのファイル。</param>
    /// <param name="hit">対象の検出内容。</param>
    public AddComposerMentionExceptionViewModel(
        TagDictionary dictionary,
        TrackTags track,
        IReadOnlyList<TrackTags> siblings,
        ComposerMismatchHit hit)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(hit);

        _dictionary = dictionary;
        _track = track;
        _siblings = siblings;
        _index = new DictionaryIndex(dictionary);

        Composer = hit.Tagged;
        FileName = Path.GetFileNameWithoutExtension(track.RelativePath);
        Title = track.Title ?? string.Empty;
        _phrase = SuggestPhrase(hit.FromTitle is null ? FileName : Title);

        UpdateNotice();
    }

    /// <summary>例外を効かせる作曲家。**変更させない。**</summary>
    public string Composer { get; }

    /// <summary>対象のファイル名。語句を決める材料として出す。</summary>
    public string FileName { get; }

    /// <summary>対象の曲名。語句を決める材料として出す。</summary>
    public string Title { get; }

    /// <summary>
    /// この設定で辞書を更新できるかを判定する。
    /// </summary>
    /// <param name="reason">できない理由。</param>
    /// <returns>更新できるなら true。</returns>
    public bool CanApply(out string reason)
    {
        reason = ComposerMismatch.CheckExceptionPhrase(_index, Phrase, Composer) switch
        {
            ComposerMentionPhraseIssue.TooFewWords =>
                "語句は 2 語以上にしてください。1 語だけでは本物の別の作曲家も検出できなくなります。",
            ComposerMentionPhraseIssue.OnlyComposerNames => "語句に作曲家名以外の語を含めてください。",
            ComposerMentionPhraseIssue.NoOtherComposer => "語句に別の作曲家名がありません。登録しても何も起きません。",
            _ => string.Empty,
        };

        if (reason.Length > 0)
        {
            return false;
        }

        // 理由の書いていない例外は、後から消してよいか判断できない。
        if (string.IsNullOrWhiteSpace(Note))
        {
            reason = "理由を入力してください。理由の書いていない例外は、後から消してよいか判断できません。";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 設定に従って辞書を更新した結果を返す。元の辞書は書き換えない。
    /// </summary>
    /// <returns>更新後の辞書。</returns>
    public TagDictionary Apply()
    {
        return DictionaryEditor.AddComposerMentionException(_dictionary, BuildEntry());
    }

    /// <summary>
    /// 入力から例外を組み立てる。
    /// </summary>
    /// <returns>例外。</returns>
    public ComposerMentionExceptionEntry BuildEntry()
    {
        return new ComposerMentionExceptionEntry
        {
            Composer = Composer,
            Phrase = Phrase.Trim(),
            Note = Note.Trim(),
        };
    }

    /// <summary>
    /// 語句の初期値を作る。先頭の番号は外す。
    ///
    /// 録音ごとに番号の振り方が違う（<c>06 2. Szene am Bach</c> / <c>02 2 Szene am Bach.</c>）ので、
    /// 残すと別の録音に効かない語句になる。
    /// </summary>
    /// <param name="value">元の値。</param>
    /// <returns>語句の初期値。</returns>
    public static string SuggestPhrase(string value)
    {
        return string.Join(' ', ComposerMismatch.SplitWords(value).SkipWhile(word => word.All(char.IsDigit)));
    }

    /// <summary>語句が変わったら、効くかを試し直す。</summary>
    partial void OnPhraseChanged(string value)
    {
        UpdateNotice();
    }

    /// <summary>
    /// 注意書きを作り直す。
    /// </summary>
    private void UpdateNotice()
    {
        if (ComposerMismatch.CheckExceptionPhrase(_index, Phrase, Composer) != ComposerMentionPhraseIssue.None)
        {
            Notice = "語句は 2 語以上で、別の作曲家名と、作曲家名以外の語を含めてください。";
            return;
        }

        DictionaryIndex trial = new(Apply());
        ComposerMismatchHit? remaining = ComposerMismatch.Find(_track, trial, _siblings);

        if (remaining is null)
        {
            Notice = "登録して再検査すると、この行は一覧から消えます。同じ語句を含む、この作曲家の他のファイルにも効きます。";
            return;
        }

        string other = remaining.FromFileName ?? remaining.FromTitle ?? remaining.FromFolder ?? string.Empty;

        Notice = $"この語句を登録しても、この行には「{other}」の検出が残ります。ファイル名と曲名の両方に効く語句か確かめてください。";
    }
}
