using MusicTagger.Core.Models;

namespace MusicTagger.Core.Tests.Models;

/// <summary>
/// <see cref="TrackTags"/> のテスト。
/// 複数値の保持は検査ルール R-205 / R-206 の前提になるため、丸め込まれないことを確認する。
/// </summary>
public sealed class TrackTagsTests
{
    /// <summary>
    /// 未設定のフィールドが null になることを確認する。
    /// </summary>
    [Fact]
    public void ReturnsNullForMissingField()
    {
        TrackTags tags = Build([]);

        Assert.Null(tags.Composer);
        Assert.Empty(tags.GetValues(TagField.Composer));
        Assert.False(tags.HasMultipleValues(TagField.Composer));
    }

    /// <summary>
    /// 単一値がそのまま返ることを確認する。
    /// </summary>
    [Fact]
    public void ReturnsSingleValueAsIs()
    {
        TrackTags tags = Build([(TagField.Composer, ["Anton Bruckner"])]);

        Assert.Equal("Anton Bruckner", tags.Composer);
        Assert.False(tags.HasMultipleValues(TagField.Composer));
    }

    /// <summary>
    /// 複数値は表示用に連結されるが、格納状態としては複数のままであることを確認する。
    /// </summary>
    [Fact]
    public void JoinsMultipleValuesForDisplayButKeepsThemSeparate()
    {
        TrackTags tags = Build([(TagField.AlbumArtist, ["Peter Pears(T)", "Hermann Prey(BR)"])]);

        Assert.Equal("Peter Pears(T); Hermann Prey(BR)", tags.AlbumArtist);
        Assert.True(tags.HasMultipleValues(TagField.AlbumArtist));
        Assert.Equal(2, tags.GetValues(TagField.AlbumArtist).Count);
    }

    /// <summary>
    /// 空文字や空白だけの値が除かれることを確認する。
    /// </summary>
    [Fact]
    public void DropsEmptyValues()
    {
        TrackTags tags = Build([(TagField.Artist, ["", "   ", "Karl Böhm"])]);

        Assert.Equal("Karl Böhm", tags.Artist);
        Assert.False(tags.HasMultipleValues(TagField.Artist));
    }

    /// <summary>
    /// すべての値が空ならフィールド自体が存在しないことを確認する。
    /// </summary>
    [Fact]
    public void OmitsFieldWhenAllValuesEmpty()
    {
        TrackTags tags = Build([(TagField.Genre, ["", "  "])]);

        Assert.Null(tags.Genre);
    }

    /// <summary>
    /// テスト用の <see cref="TrackTags"/> を組み立てる。
    /// </summary>
    private static TrackTags Build(IEnumerable<(TagField Field, string[] Values)> fields)
    {
        return new TrackTags
        {
            RelativePath = "test.m4a",
            FullPath = @"C:\test.m4a",
            Format = AudioFormat.M4a,
            Fields = TrackTags.BuildFields(
                fields.Select(pair => new KeyValuePair<TagField, IReadOnlyList<string>>(pair.Field, pair.Values))),
            RawTags = new Dictionary<string, string[]>(),
        };
    }
}
