using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Editing;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Tests.Editing;

/// <summary>
/// 手編集の検査のテスト。
///
/// **すべて警告であり、入力を止めない。** 手で入れた値をツールが拒むと、
/// 原則の例外（配役情報や個別例外）を扱えなくなる。
/// </summary>
public sealed class ManualEditValidatorTests
{
    /// <summary>検査に使う辞書の索引。</summary>
    private static readonly DictionaryIndex DICTIONARY = new(DictionaryLoader.LoadDefault());

    /// <summary>
    /// 原則どおりの入力では何も出ないことを確認する。警告が常時出ると読み飛ばされる。
    /// </summary>
    [Fact]
    public void AcceptsValidInputSilently()
    {
        TrackTags track = Track("01.flac", (TagField.Conductor, "Wand"));

        Assert.Empty(Validate(track, TagField.Conductor, "Günter Wand"));
    }

    /// <summary>
    /// 値に <c>;</c> が含まれる場合に知らせることを確認する。
    /// AIMP は保存時にこれを区切りとして分割する（docs/TAGGING_POLICY.md 3.4）。
    /// </summary>
    [Fact]
    public void WarnsOnSemicolon()
    {
        TrackTags track = Track("01.flac", (TagField.Artist, "x"));

        Assert.Contains(
            Validate(track, TagField.Artist, "Karl Böhm; Wiener Philharmoniker"),
            warning => warning.Message.Contains("分割", StringComparison.Ordinal));
    }

    /// <summary>
    /// 複数値として格納されているフィールドの編集を知らせることを確認する。
    /// M4A では複数値を書き分けられない（docs/adr/0001-tag-io-library.md）。
    /// </summary>
    [Fact]
    public void WarnsWhenCollapsingMultipleValues()
    {
        TrackTags track = new()
        {
            RelativePath = "01.m4a",
            FullPath = "C:\\library\\01.m4a",
            Format = AudioFormat.M4a,
            Fields = TrackTags.BuildFields(
                [new KeyValuePair<TagField, IReadOnlyList<string>>(TagField.AlbumArtist, ["A", "B"])]),
            RawTags = new Dictionary<string, string[]>(),
        };

        Assert.Contains(
            Validate(track, TagField.AlbumArtist, "Wiener Philharmoniker"),
            warning => warning.Message.Contains("1 値にまとまります", StringComparison.Ordinal));
    }

    /// <summary>
    /// 配役情報として保護されている値の書き換えを知らせることを確認する
    /// （docs/TAGGING_POLICY.md 2.3）。
    ///
    /// 検査ルールは保護対象に触らないが、手編集はその保護を越えられる。
    /// </summary>
    [Fact]
    public void WarnsWhenOverwritingProtectedAlbumArtist()
    {
        string protectedValue = DICTIONARY.Dictionary.ProtectedAlbumArtists[0];

        TrackTags track = Track("パルジファル/01.m4a", (TagField.AlbumArtist, protectedValue));

        Assert.Contains(
            Validate(track, TagField.AlbumArtist, "Berliner Philharmoniker"),
            warning => warning.Message.Contains("配役情報", StringComparison.Ordinal));
    }

    /// <summary>
    /// ジャンル・年・トラック番号の書式を確認する（docs/TAGGING_POLICY.md 2.4）。
    /// </summary>
    [Theory]
    [InlineData(TagField.Genre, "Classical")]
    [InlineData(TagField.Date, "1993-01-22T08:00:00Z")]
    [InlineData(TagField.TrackNumber, "1")]
    [InlineData(TagField.DiscNumber, "1")]
    public void WarnsOnFieldFormat(TagField field, string value)
    {
        TrackTags track = Track("01.flac", (field, "x"));

        Assert.NotEmpty(Validate(track, field, value));
    }

    /// <summary>
    /// 正しい書式では警告が出ないことを確認する。
    /// </summary>
    [Theory]
    [InlineData(TagField.Genre, "Classic")]
    [InlineData(TagField.Date, "1993")]
    [InlineData(TagField.TrackNumber, "1/1")]
    public void AcceptsCorrectFieldFormat(TagField field, string value)
    {
        TrackTags track = Track("01.flac", (field, "x"));

        Assert.Empty(Validate(track, field, value));
    }

    /// <summary>
    /// 人名・団体名に日本語表記を入れた場合に知らせることを確認する（docs/TAGGING_POLICY.md 3.1）。
    /// </summary>
    [Fact]
    public void WarnsOnJapaneseNameValue()
    {
        TrackTags track = Track("01.flac", (TagField.Conductor, "x"));

        Assert.Contains(
            Validate(track, TagField.Conductor, "カラヤン"),
            warning => warning.Message.Contains("ラテン文字", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>artist</c> に作曲家名を入れようとした場合に知らせることを確認する
    /// （docs/TAGGING_POLICY.md 2.1）。
    /// </summary>
    [Fact]
    public void WarnsWhenComposerNameGoesIntoPerformerField()
    {
        TrackTags track = Track("01.flac", (TagField.Artist, "x"));

        Assert.Contains(
            Validate(track, TagField.Artist, "Richard Wagner"),
            warning => warning.Message.Contains("作曲家", StringComparison.Ordinal));
    }

    /// <summary>
    /// 辞書に無い名前を知らせることを確認する。段階 5 の辞書追加導線へ回してもらう。
    /// </summary>
    [Fact]
    public void WarnsOnNameMissingFromDictionary()
    {
        TrackTags track = Track("01.flac", (TagField.Conductor, "x"));

        Assert.Contains(
            Validate(track, TagField.Conductor, "Someone Unknown"),
            warning => warning.Message.Contains("辞書に無い", StringComparison.Ordinal));
    }

    /// <summary>
    /// 値を消す編集では書式の警告を出さないことを確認する。
    /// 空にすることは原則が認める操作である（docs/TAGGING_POLICY.md 7.4）。
    /// </summary>
    [Fact]
    public void DoesNotWarnAboutFormatWhenClearing()
    {
        TrackTags track = Track("01.flac", (TagField.AlbumArtist, "Gustav Mahler"));

        Assert.Empty(Validate(track, TagField.AlbumArtist, string.Empty));
    }

    /// <summary>
    /// その形式で扱えないフィールドの編集を知らせることを確認する（docs/TAGGING_POLICY.md 4.4）。
    ///
    /// ID3 では <c>comment</c> を書き込まない。編集はできてしまうので、適用しても
    /// 何も起きないことを事前に伝えないと、利用者は理由に辿り着けない。
    /// </summary>
    [Fact]
    public void WarnsWhenFieldIsUnsupportedByFormat()
    {
        TrackTags track = Id3Track("01.aif", (TagField.Comment, "x"));

        ManualEditWarning warning = Assert.Single(
            Validate(track, TagField.Comment, "Haas edition"),
            warning => warning.Message.Contains("扱いません", StringComparison.Ordinal));

        // 形式は拡張子で示す。利用者は Id3 という語で自分のファイルを見分けられない。
        Assert.Contains(".aif", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Id3", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 値を消す編集でも、扱えないフィールドなら知らせることを確認する。
    ///
    /// **書式の警告と違い、こちらは <c>ClearsValue</c> で抜ける前に見る必要がある。**
    /// 消す操作も同じく書き込まれないため。
    /// </summary>
    [Fact]
    public void WarnsUnsupportedFieldEvenWhenClearing()
    {
        TrackTags track = Id3Track("01.aif", (TagField.Comment, "Haas edition"));

        Assert.Contains(
            Validate(track, TagField.Comment, string.Empty),
            warning => warning.Message.Contains("扱いません", StringComparison.Ordinal));
    }

    /// <summary>
    /// 扱える形式では警告を出さないことを確認する。
    /// </summary>
    [Fact]
    public void DoesNotWarnAboutSupportedFieldOnFlac()
    {
        TrackTags track = Track("01.flac", (TagField.Comment, "x"));

        Assert.Empty(Validate(track, TagField.Comment, "Haas edition"));
    }

    /// <summary>
    /// <c>comment</c> でも <c>;</c> を知らせることを確認する。
    ///
    /// 2.4 は「正規形を定めない」と言うが、それは内容の話である。3.4 の
    /// 「AIMP が <c>;</c> で分割する」はフィールドを問わない格納上の挙動で、別の層にある。
    /// ここで黙ると、AIMP が保存した瞬間に 2 値へ割れても誰も気づけない。
    /// </summary>
    [Fact]
    public void WarnsOnSemicolonInComment()
    {
        TrackTags track = Track("01.flac", (TagField.Comment, "x"));

        Assert.Contains(
            Validate(track, TagField.Comment, "ハース版; 1980 年ライヴ"),
            warning => warning.Message.Contains("分割", StringComparison.Ordinal));
    }

    /// <summary>
    /// <c>comment</c> には書式・辞書の警告を出さないことを確認する。
    /// 自由記述なので正規形が無い（docs/TAGGING_POLICY.md 2.4）。
    /// </summary>
    [Fact]
    public void DoesNotApplyNameOrFormatChecksToComment()
    {
        TrackTags track = Track("01.flac", (TagField.Comment, "x"));

        Assert.Empty(Validate(track, TagField.Comment, "ノヴァーク版 1890"));
    }

    /// <summary>
    /// 1 件の編集を検査する。
    /// </summary>
    private static IReadOnlyList<ManualEditWarning> Validate(TrackTags track, TagField field, string value)
    {
        ManualEditSet edits = new();
        edits.Set(track, field, value);

        return ManualEditValidator.Validate(edits.ToChanges(), [track], DICTIONARY);
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

    /// <summary>
    /// ID3 形式のテスト用タグを作る。形式によって扱えるフィールドが違うことの検証に使う。
    /// </summary>
    private static TrackTags Id3Track(string relativePath, params (TagField Field, string? Value)[] fields)
    {
        return Track(relativePath, fields) with { Format = AudioFormat.Id3 };
    }
}
