using System.IO;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Editing;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// ファイル一覧タブの 1 行のテスト。
/// 絞り込みの母集合（空欄・検索）に何を含めるかを固定する。
/// </summary>
public sealed class TrackRowViewModelTests
{
    /// <summary>
    /// **「空欄のある行のみ」が <c>comment</c> を見ないことを確認する。**
    ///
    /// <c>comment</c> は版・稿の注記で、必要なファイルにしか入らない。空が正常なので、
    /// 見る対象に含めるとほぼ全行が当たり、R-401 / R-402 の対象を探すという
    /// この絞り込みの目的が果たせなくなる。
    /// </summary>
    [Fact]
    public void HasEmptyFieldIgnoresComment()
    {
        TrackRowViewModel row = Row(Track(
            (TagField.Title, "Symphony No. 8 - I. Allegro moderato"),
            (TagField.Artist, "Günter Wand"),
            (TagField.AlbumArtist, "Berliner Philharmoniker"),
            (TagField.Composer, "Anton Bruckner"),
            (TagField.Conductor, "Günter Wand"),
            (TagField.Album, "Bruckner: Symphony No. 8 - 2001/Berliner Philharmoniker"),
            (TagField.Genre, "Classic"),
            (TagField.Date, "2001"),
            (TagField.TrackNumber, "1/4"),
            (TagField.DiscNumber, "1/1")));

        Assert.False(row.HasEmptyField);
    }

    /// <summary>
    /// <c>comment</c> 以外が欠けていれば従来どおり拾うことを確認する。
    /// 上のテストが「常に false」で通っていないことを示す。
    /// </summary>
    [Fact]
    public void HasEmptyFieldStillDetectsOtherFields()
    {
        Assert.True(Row(Track((TagField.Title, "Allegro"))).HasEmptyField);
    }

    /// <summary>
    /// 検索の母集合に <c>comment</c> を含めることを確認する。
    ///
    /// 「ハース版」で絞る → 全選択 → 一括入力、という導線が版・稿を扱う主な手順になる。
    /// 含めないと、入れた値を後から辿る手段が無くなる。
    /// </summary>
    [Fact]
    public void SearchTextIncludesComment()
    {
        TrackRowViewModel row = Row(Track((TagField.Comment, "ハース版")));

        Assert.Contains("ハース版", row.SearchText, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>comment</c> の編集が保留中の手編集として記録されることを確認する。
    /// </summary>
    [Fact]
    public void RecordsCommentEditAsPendingEdit()
    {
        ManualEditSet edits = new();
        TrackRowViewModel row = Row(Track((TagField.Comment, "ハース版")), edits);

        row.Comment = "ノヴァーク版";

        TagChange change = Assert.Single(edits.ToChanges());

        Assert.Equal(TagField.Comment, change.Field);
        Assert.Equal(["ノヴァーク版"], change.AfterValues);
        Assert.True(row.IsEdited);
    }

    /// <summary>
    /// 行を作る。
    /// </summary>
    private static TrackRowViewModel Row(TrackTags tags, ManualEditSet? edits = null)
    {
        return new TrackRowViewModel(tags, edits ?? new ManualEditSet());
    }

    /// <summary>
    /// テスト用のタグを組み立てる。
    /// </summary>
    private static TrackTags Track(params (TagField Field, string Value)[] fields)
    {
        return new TrackTags
        {
            RelativePath = Path.Combine("ブルックナー", "01.flac"),
            FullPath = Path.Combine(@"D:\Library", "ブルックナー", "01.flac"),
            Format = AudioFormat.Flac,
            Fields = TrackTags.BuildFields(
                fields.Select(pair =>
                    new KeyValuePair<TagField, IReadOnlyList<string>>(pair.Field, [pair.Value]))),
            RawTags = new Dictionary<string, string[]>(),
        };
    }
}
