using MusicTagger.Core.Editing;
using MusicTagger.Core.Models;

namespace MusicTagger.Core.Tests.Editing;

/// <summary>
/// 手編集の保持のテスト。
///
/// 手編集は即座に書き込まず差分として溜める。溜めた内容がそのまま適用フローへ渡るので、
/// 差分の作られ方が仕様どおりであることを確認する。
/// </summary>
public sealed class ManualEditSetTests
{
    /// <summary>
    /// 編集が差分になることを確認する。
    /// </summary>
    [Fact]
    public void ProducesChangeForEditedField()
    {
        ManualEditSet edits = new();
        TrackTags track = Track("ブル8/01.flac", (TagField.Conductor, "Wand"));

        Assert.True(edits.Set(track, TagField.Conductor, "Günter Wand"));

        TagChange change = Assert.Single(edits.ToChanges());

        Assert.Equal(ManualEditConst.RULE_ID, change.RuleId);
        Assert.Equal(Severity.Manual, change.Severity);
        Assert.Equal("Wand", change.BeforeText);
        Assert.Equal("Günter Wand", change.AfterText);
        Assert.True(change.HasFix);
        Assert.True(change.IsSelected);
    }

    /// <summary>
    /// 元の値に戻したら編集そのものが消えることを確認する。
    /// 差分ゼロの行を残すと、適用の確認画面で「何も変わらない項目」を数えることになる。
    /// </summary>
    [Fact]
    public void RemovesEditWhenValueReturnsToOriginal()
    {
        ManualEditSet edits = new();
        TrackTags track = Track("01.flac", (TagField.Composer, "Anton Bruckner"));

        edits.Set(track, TagField.Composer, "Bruckner");
        Assert.Equal(1, edits.Count);

        Assert.False(edits.Set(track, TagField.Composer, "Anton Bruckner"));
        Assert.Equal(0, edits.Count);
        Assert.Empty(edits.ToChanges());
    }

    /// <summary>
    /// 空欄にする編集が「値を消す」差分になることを確認する。
    ///
    /// 修正案が空であることは、ルールにとっては「決められなかった」を意味する。
    /// 手編集の削除意図と取り違えないよう、別の形で表す必要がある。
    /// </summary>
    [Fact]
    public void ClearingProducesExplicitClearChange()
    {
        ManualEditSet edits = new();
        TrackTags track = Track("01.flac", (TagField.AlbumArtist, "Gustav Mahler"));

        edits.Set(track, TagField.AlbumArtist, "   ");

        TagChange change = Assert.Single(edits.ToChanges());

        Assert.True(change.ClearsValue);
        Assert.Empty(change.AfterValues);

        // 「決められなかった」なら適用対象にならないが、削除の指示は適用対象になる。
        Assert.True(change.HasFix);
        Assert.True(change.IsSelected);
    }

    /// <summary>
    /// もともと空の項目を空にしても編集にならないことを確認する。
    /// </summary>
    [Fact]
    public void ClearingEmptyFieldIsNotAnEdit()
    {
        ManualEditSet edits = new();
        TrackTags track = Track("01.flac", (TagField.Title, "x"));

        Assert.False(edits.Set(track, TagField.Conductor, string.Empty));
        Assert.Equal(0, edits.Count);
    }

    /// <summary>
    /// 複数行への一括入力が、件数を根拠に残すことを確認する。
    /// docs/SPEC.md 5.2 が「アルバム単位の編集で必須」とする操作。
    /// </summary>
    [Fact]
    public void BulkInputRecordsCountInRationale()
    {
        ManualEditSet edits = new();

        TrackTags[] tracks =
        [
            Track("ブル8/01.flac", (TagField.Conductor, null)),
            Track("ブル8/02.flac", (TagField.Conductor, null)),
            Track("ブル8/03.flac", (TagField.Conductor, null)),
        ];

        Assert.Equal(3, edits.SetMany(tracks, TagField.Conductor, "Günter Wand"));

        IReadOnlyList<TagChange> changes = edits.ToChanges();

        Assert.Equal(3, changes.Count);
        Assert.All(changes, change => Assert.Contains("3 ファイルに一括入力", change.Rationale, StringComparison.Ordinal));
    }

    /// <summary>
    /// 一括入力で既に同じ値だったファイルが編集にならないことを確認する。
    /// </summary>
    [Fact]
    public void BulkInputSkipsFilesAlreadyHoldingTheValue()
    {
        ManualEditSet edits = new();

        TrackTags[] tracks =
        [
            Track("01.flac", (TagField.Genre, "Classic")),
            Track("02.flac", (TagField.Genre, null)),
        ];

        Assert.Equal(1, edits.SetMany(tracks, TagField.Genre, "Classic"));
        Assert.Equal("02.flac", Assert.Single(edits.ToChanges()).RelativePath);
    }

    /// <summary>
    /// 同じフィールドを 2 回編集したら、最後の値だけが残ることを確認する。
    /// </summary>
    [Fact]
    public void KeepsOnlyLatestValuePerField()
    {
        ManualEditSet edits = new();
        TrackTags track = Track("01.flac", (TagField.Album, "Symphony No.5"));

        edits.Set(track, TagField.Album, "A");
        edits.Set(track, TagField.Album, "B");

        Assert.Equal("B", Assert.Single(edits.ToChanges()).AfterText);
    }

    /// <summary>
    /// 編集後の表示値が返ることを確認する。編集していないフィールドは元の値のまま。
    /// </summary>
    [Fact]
    public void ReturnsDisplayValueReflectingEdit()
    {
        ManualEditSet edits = new();
        TrackTags track = Track("01.flac", (TagField.Artist, "Solt"), (TagField.Album, "x"));

        edits.Set(track, TagField.Artist, "Georg Solti");

        Assert.Equal("Georg Solti", edits.GetDisplayValue(track, TagField.Artist));
        Assert.Equal("x", edits.GetDisplayValue(track, TagField.Album));
        Assert.True(edits.IsEdited("01.flac", TagField.Artist));
        Assert.False(edits.IsEdited("01.flac", TagField.Album));
    }

    /// <summary>
    /// ファイル単位で編集を取り消せることを確認する。
    /// </summary>
    [Fact]
    public void ResetsEditsPerFile()
    {
        ManualEditSet edits = new();
        TrackTags first = Track("01.flac", (TagField.Artist, "a"));
        TrackTags second = Track("02.flac", (TagField.Artist, "b"));

        edits.Set(first, TagField.Artist, "x");
        edits.Set(first, TagField.Album, "y");
        edits.Set(second, TagField.Artist, "z");

        edits.Reset("01.flac");

        Assert.Equal(1, edits.Count);
        Assert.False(edits.IsEdited("01.flac"));
        Assert.True(edits.IsEdited("02.flac"));
    }

    /// <summary>
    /// 前後の空白が落ちることを確認する。貼り付けで紛れ込んだ空白がタグに入らないようにする。
    /// </summary>
    [Fact]
    public void TrimsInputValue()
    {
        ManualEditSet edits = new();
        TrackTags track = Track("01.flac", (TagField.Artist, "x"));

        edits.Set(track, TagField.Artist, "  Karl Böhm  ");

        Assert.Equal("Karl Böhm", Assert.Single(edits.ToChanges()).AfterText);
    }

    /// <summary>
    /// 変更の通知が飛ぶことを確認する。画面の再描画に使う。
    /// </summary>
    [Fact]
    public void RaisesChangedEvent()
    {
        ManualEditSet edits = new();
        TrackTags track = Track("01.flac", (TagField.Artist, "x"));
        int raised = 0;

        edits.Changed += (_, _) => raised++;

        edits.Set(track, TagField.Artist, "y");
        edits.Set(track, TagField.Artist, "x");
        edits.Clear();

        // 設定 / 取り消し。Clear は対象が無いので発火しない。
        Assert.Equal(2, raised);
    }

    /// <summary>
    /// テスト用のタグを作る。
    /// </summary>
    private static TrackTags Track(string relativePath, params (TagField Field, string? Value)[] fields)
    {
        return new TrackTags
        {
            RelativePath = relativePath,
            FullPath = Path.Combine("C:\\library", relativePath),
            Format = AudioFormat.Flac,
            Fields = TrackTags.BuildFields(
                fields.Where(field => field.Value is not null)
                    .Select(field => new KeyValuePair<TagField, IReadOnlyList<string>>(field.Field, [field.Value!]))),
            RawTags = new Dictionary<string, string[]>(),
        };
    }
}
