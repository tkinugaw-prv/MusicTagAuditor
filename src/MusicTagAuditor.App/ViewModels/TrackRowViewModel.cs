using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using MusicTagAuditor.Core.Editing;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.App.ViewModels;

/// <summary>
/// ファイル一覧タブの 1 行。
///
/// 段階 6 で編集できるようになった。**セルを直してもファイルには書き込まない。**
/// 入力は <see cref="ManualEditSet"/> に溜め、差分を確認してから一括で適用する
/// （docs/SPEC.md 1章の最重要方針）。
/// </summary>
public sealed class TrackRowViewModel : ObservableObject
{
    /// <summary>保留中の手編集。</summary>
    private readonly ManualEditSet _edits;

    /// <summary>
    /// 行を作る。
    /// </summary>
    /// <param name="tags">表示するタグ。</param>
    /// <param name="edits">保留中の手編集。</param>
    public TrackRowViewModel(TrackTags tags, ManualEditSet edits)
    {
        Tags = tags;
        _edits = edits;
    }

    /// <summary>元になったタグ。</summary>
    public TrackTags Tags { get; }

    /// <summary>ライブラリルートからの相対パス。</summary>
    public string RelativePath => Tags.RelativePath;

    /// <summary>相対パスのうちフォルダ部分。ツリーでの絞り込みに使う。</summary>
    public string FolderPath => Path.GetDirectoryName(Tags.RelativePath) ?? string.Empty;

    /// <summary>ファイル名。</summary>
    public string FileName => Path.GetFileName(Tags.RelativePath);

    /// <summary>タグの格納形式。</summary>
    public string Format => Tags.Format.ToString();

    /// <summary>曲名。</summary>
    public string? Title
    {
        get => Get(TagField.Title);
        set => Set(TagField.Title, value);
    }

    /// <summary>その録音の主役。</summary>
    public string? Artist
    {
        get => Get(TagField.Artist);
        set => Set(TagField.Artist, value);
    }

    /// <summary>演奏団体。</summary>
    public string? AlbumArtist
    {
        get => Get(TagField.AlbumArtist);
        set => Set(TagField.AlbumArtist, value);
    }

    /// <summary>作曲家。</summary>
    public string? Composer
    {
        get => Get(TagField.Composer);
        set => Set(TagField.Composer, value);
    }

    /// <summary>指揮者。</summary>
    public string? Conductor
    {
        get => Get(TagField.Conductor);
        set => Set(TagField.Conductor, value);
    }

    /// <summary>アルバム名。</summary>
    public string? Album
    {
        get => Get(TagField.Album);
        set => Set(TagField.Album, value);
    }

    /// <summary>ジャンル。</summary>
    public string? Genre
    {
        get => Get(TagField.Genre);
        set => Set(TagField.Genre, value);
    }

    /// <summary>録音年。</summary>
    public string? Date
    {
        get => Get(TagField.Date);
        set => Set(TagField.Date, value);
    }

    /// <summary>トラック番号。</summary>
    public string? TrackNumber
    {
        get => Get(TagField.TrackNumber);
        set => Set(TagField.TrackNumber, value);
    }

    /// <summary>ディスク番号。</summary>
    public string? DiscNumber
    {
        get => Get(TagField.DiscNumber);
        set => Set(TagField.DiscNumber, value);
    }

    /// <summary>
    /// 自由記述の注記。版・稿の情報を置く（docs/TAGGING_POLICY.md 2.4）。
    /// ID3（.mp3 / .aif）では扱わないため常に空で、編集すると気づきが出る（同 4.4）。
    /// </summary>
    public string? Comment
    {
        get => Get(TagField.Comment);
        set => Set(TagField.Comment, value);
    }

    /// <summary>この行に保留中の編集があるか。</summary>
    public bool IsEdited => _edits.IsEdited(RelativePath);

    /// <summary>
    /// いずれかのフィールドが空欄か。R-401 / R-402 の対象を探すときの絞り込みに使う。
    /// 自由記述のフィールドは見ない（<c>comment</c> は空が正常なので、含めると全行が当たる）。
    /// </summary>
    public bool HasEmptyField =>
        ManualEditConst.EMPTY_CHECK_FIELDS.Any(tagField => string.IsNullOrEmpty(Get(tagField)));

    /// <summary>絞り込みの対象になる文字列をまとめたもの。</summary>
    public string SearchText => string.Join(
        '\n',
        new[] { RelativePath }.Concat(ManualEditConst.EDITABLE_FIELDS.Select(Get).Where(value => value is not null)!));

    /// <summary>
    /// いずれかのフィールドが複数値として格納されているか。
    /// AIMP が <c>;</c> で分割した状態であり、表示上は連結されて見えるため印を付けて区別する
    /// （docs/TAGGING_POLICY.md 3.4）。
    /// </summary>
    public bool HasSplitValues => Enum.GetValues<TagField>().Any(Tags.HasMultipleValues);

    /// <summary>複数値を持つフィールドの一覧。ツールチップに出す。</summary>
    public string SplitValueSummary
    {
        get
        {
            string[] fields =
            [
                .. Enum.GetValues<TagField>()
                    .Where(Tags.HasMultipleValues)
                    .Select(tagField => $"{tagField}: {Tags.GetValues(tagField).Count} 値"),
            ];

            return fields.Length == 0
                ? string.Empty
                : "複数値として格納されています — " + string.Join(" / ", fields);
        }
    }

    /// <summary>
    /// 編集が外部から変わったことを画面に伝える。一括入力や編集の破棄のあとに呼ぶ。
    /// </summary>
    public void NotifyEditsChanged()
    {
        // どのフィールドが変わったか分からないので、行全体を出し直す。
        OnPropertyChanged(new PropertyChangedEventArgs(null));
    }

    /// <summary>
    /// 編集後の値を返す。編集していなければ読み取った値をそのまま返す。
    /// </summary>
    private string? Get(TagField field)
    {
        return _edits.GetDisplayValue(Tags, field);
    }

    /// <summary>
    /// 入力を保留中の編集として記録する。ファイルには書き込まない。
    ///
    /// 変更を通知するのは編集中のセルと、行全体の状態を表す 2 つだけにする。
    /// セルの編集確定中に行全体を出し直すと、確定処理と再描画が噛み合わない。
    /// </summary>
    private void Set(TagField field, string? value, [CallerMemberName] string? propertyName = null)
    {
        _edits.Set(Tags, field, value);

        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(IsEdited));
        OnPropertyChanged(nameof(HasEmptyField));
    }
}
