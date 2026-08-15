using System.Text;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.TagIo.Tests.Fixtures;

namespace MusicTagAuditor.TagIo.Tests;

/// <summary>
/// フォーマット別のタグ読み取りのテスト。
/// 検体はテスト実行時に生成する（市販音源の断片をリポジトリに置かないため）。
/// </summary>
public sealed class TagReaderTests : IDisposable
{
    /// <summary>検体を置く一時フォルダ。</summary>
    private readonly string _workDir;

    /// <summary>テスト対象。</summary>
    private readonly TagReader _reader = new();

    /// <summary>
    /// 検体用の一時フォルダを用意する。
    /// </summary>
    public TagReaderTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "MusicTagAuditor.tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    /// <summary>
    /// 一時フォルダを削除する。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_workDir))
        {
            Directory.Delete(_workDir, recursive: true);
        }
    }

    /// <summary>
    /// M4A の各 atom が論理フィールドへ対応づくことを確認する。
    /// 特に指揮者が <c>©con</c> から読めること。
    /// </summary>
    [Fact]
    public void MapsM4aAtomsToFields()
    {
        string path = WriteM4a(
        [
            ("©nam", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("Symphony No.8 - I. Allegro moderato")]),
            ("©ART", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("Herbert von Karajan")]),
            ("aART", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("Wiener Philharmoniker")]),
            ("©wrt", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("Anton Bruckner")]),
            ("©con", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("Herbert von Karajan")]),
            ("©alb", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("Bruckner: Symphony No.8")]),
            ("©gen", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("Classic")]),
            ("©day", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("1988")]),
        ]);

        TrackTags tags = _reader.Read(path, "test.m4a");

        Assert.Equal(AudioFormat.M4a, tags.Format);
        Assert.Equal("Symphony No.8 - I. Allegro moderato", tags.Title);
        Assert.Equal("Herbert von Karajan", tags.Artist);
        Assert.Equal("Wiener Philharmoniker", tags.AlbumArtist);
        Assert.Equal("Anton Bruckner", tags.Composer);
        Assert.Equal("Herbert von Karajan", tags.Conductor);
        Assert.Equal("Bruckner: Symphony No.8", tags.Album);
        Assert.Equal("Classic", tags.Genre);
        Assert.Equal("1988", tags.Date);
    }

    /// <summary>
    /// TagLib# が書く <c>cond</c> を指揮者として読まないことを確認する。
    /// AIMP は <c>©con</c> しか読まないため、<c>cond</c> を指揮者扱いすると
    /// 「アプリでは見えるが AIMP では見えない」状態を検出できなくなる。
    /// </summary>
    [Fact]
    public void DoesNotTreatCondAtomAsConductor()
    {
        string path = WriteM4a(
        [
            (TagIoConst.ATOM_CONDUCTOR_WRONG, MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("Yevgeny Mravinsky")]),
        ]);

        TrackTags tags = _reader.Read(path, "test.m4a");

        Assert.Null(tags.Conductor);
        Assert.Equal(["Yevgeny Mravinsky"], tags.RawTags[TagIoConst.ATOM_CONDUCTOR_WRONG]);
    }

    /// <summary>
    /// <c>trkn</c> / <c>disk</c> のバイナリ値が「番号/総数」に変換されることを確認する。
    /// </summary>
    [Fact]
    public void DecodesTrackAndDiscNumbers()
    {
        string path = WriteM4a(
        [
            ("trkn", MinimalAudioFileBuilder.DATA_TYPE_BINARY, [[0x00, 0x00, 0x00, 0x03, 0x00, 0x06, 0x00, 0x00]]),
            ("disk", MinimalAudioFileBuilder.DATA_TYPE_BINARY, [[0x00, 0x00, 0x00, 0x01, 0x00, 0x01]]),
        ]);

        TrackTags tags = _reader.Read(path, "test.m4a");

        Assert.Equal("3/6", tags.TrackNumber);
        Assert.Equal("1/1", tags.DiscNumber);
    }

    /// <summary>
    /// <c>;</c> を含む 1 値と、分割済みの複数値が区別できることを確認する。
    /// docs/TAGGING_POLICY.md 3.4 / 検査ルール R-205 の前提になる。
    /// </summary>
    [Fact]
    public void DistinguishesSemicolonValueFromSplitValues()
    {
        string singlePath = WriteM4a(
        [
            ("aART", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("Peter Pears(T); Hermann Prey(BR)")]),
        ]);

        string splitPath = WriteM4a(
        [
            ("aART", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("Peter Pears(T)"), Utf8("Hermann Prey(BR)")]),
        ],
        fileName: "split.m4a");

        TrackTags single = _reader.Read(singlePath, "single.m4a");
        TrackTags split = _reader.Read(splitPath, "split.m4a");

        Assert.False(single.HasMultipleValues(TagField.AlbumArtist));
        Assert.True(split.HasMultipleValues(TagField.AlbumArtist));

        // 表示上はどちらも同じ文字列になる。だからこそ格納値の件数で区別する必要がある。
        Assert.Equal(single.AlbumArtist, split.AlbumArtist);
    }

    /// <summary>
    /// FLAC の Vorbis comment が読めることを確認する。
    /// </summary>
    [Fact]
    public void ReadsFlacVorbisComment()
    {
        string path = Path.Combine(_workDir, "test.flac");
        File.WriteAllBytes(path, MinimalAudioFileBuilder.BuildFlac());

        using (TagLib.File file = TagLib.File.Create(path))
        {
            TagLib.Ogg.XiphComment xiph = (TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph, create: true);
            xiph.SetField("CONDUCTOR", "Yevgeny Mravinsky");
            xiph.SetField("COMPOSER", "Dmitri Shostakovich");
            xiph.SetField("GENRE", "Classic");
            file.Save();
        }

        TrackTags tags = _reader.Read(path, "test.flac");

        Assert.Equal(AudioFormat.Flac, tags.Format);
        Assert.Equal("Yevgeny Mravinsky", tags.Conductor);
        Assert.Equal("Dmitri Shostakovich", tags.Composer);
        Assert.Equal("Classic", tags.Genre);
    }

    /// <summary>
    /// AIFF の ID3 チャンクが読めることを確認する（docs/SPEC.md 4.1 V6）。
    /// </summary>
    [Fact]
    public void ReadsAiffId3Chunk()
    {
        string path = Path.Combine(_workDir, "test.aif");
        File.WriteAllBytes(path, MinimalAudioFileBuilder.BuildAiff());

        WriteId3Tags(path);

        TrackTags tags = _reader.Read(path, "test.aif");

        Assert.Equal(AudioFormat.Id3, tags.Format);
        Assert.Equal("Yevgeny Mravinsky", tags.Conductor);
        Assert.Equal("Edvard Grieg", tags.Composer);
    }

    /// <summary>
    /// MP3 の ID3v2 が読めることを確認する。
    /// </summary>
    [Fact]
    public void ReadsMp3Id3v2()
    {
        string path = Path.Combine(_workDir, "test.mp3");
        File.WriteAllBytes(path, MinimalAudioFileBuilder.BuildMp3());

        WriteId3Tags(path);

        TrackTags tags = _reader.Read(path, "test.mp3");

        Assert.Equal(AudioFormat.Id3, tags.Format);
        Assert.Equal("Yevgeny Mravinsky", tags.Conductor);
        Assert.Equal("Edvard Grieg", tags.Composer);
    }

    /// <summary>
    /// 対応していない拡張子では例外になることを確認する。
    /// </summary>
    [Fact]
    public void ThrowsForUnsupportedExtension()
    {
        string path = Path.Combine(_workDir, "test.wav");
        File.WriteAllBytes(path, [0x00]);

        Assert.Throws<NotSupportedException>(() => _reader.Read(path, "test.wav"));
    }

    /// <summary>
    /// ID3v2 の指揮者・作曲家を書き込む。
    /// </summary>
    private static void WriteId3Tags(string path)
    {
        using TagLib.File file = TagLib.File.Create(path);
        TagLib.Id3v2.Tag id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);
        id3.Conductor = "Yevgeny Mravinsky";
        id3.Composers = ["Edvard Grieg"];
        file.Save();
    }

    /// <summary>
    /// M4A の <c>©cmt</c> が <c>comment</c> として読めることを確認する。
    /// </summary>
    [Fact]
    public void MapsCmtAtomToComment()
    {
        string path = WriteM4a(
        [
            ("©cmt", MinimalAudioFileBuilder.DATA_TYPE_UTF8, [Utf8("ハース版")]),
        ]);

        Assert.Equal("ハース版", _reader.Read(path, "test.m4a").Comment);
    }

    /// <summary>
    /// **ID3 の <c>COMM</c> を <c>comment</c> として読まないことを確認する。**
    ///
    /// iTunes は <c>iTunNORM</c> 等を description 付きの <c>COMM</c> に入れる。論理フィールドに
    /// 対応づけると、そのバイナリ文字列が画面のコメント欄に出てしまう
    /// （docs/TAGGING_POLICY.md 4.4）。記録としては <c>RawTags</c> に残る。
    /// </summary>
    [Theory]
    [InlineData("mp3")]
    [InlineData("aif")]
    public void DoesNotReadId3CommentField(string extension)
    {
        string path = Path.Combine(_workDir, $"test.{extension}");
        File.WriteAllBytes(
            path,
            extension == "mp3" ? MinimalAudioFileBuilder.BuildMp3() : MinimalAudioFileBuilder.BuildAiff());

        using (TagLib.File file = TagLib.File.Create(path))
        {
            TagLib.Id3v2.Tag id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);
            id3.AddFrame(new TagLib.Id3v2.CommentsFrame("iTunNORM", "eng") { Text = " 000001A5 00000174" });
            file.Save();
        }

        TrackTags tags = _reader.Read(path, $"test.{extension}");

        Assert.Null(tags.Comment);
        Assert.Contains("COMM", tags.RawTags.Keys);
    }

    /// <summary>
    /// 形式ごとに扱えるフィールドの宣言（<c>TagFieldConst.IsSupported</c>）と、
    /// 実体の対応表（<c>TagIoConst</c> の 3 辞書）が一致することを確認する。
    ///
    /// **両者を同時に見られるのはこのテストだけである。** 依存の向きは TagIo → Core なので、
    /// Core 側からは対応表を参照できない。宣言だけ直して辞書を直し忘れると、画面は
    /// 「扱える」と言うのに書き込みが黙って無視される、という食い違いが起きる。
    /// </summary>
    [Fact]
    public void TagIoConstMatchesSupportedFields()
    {
        Dictionary<AudioFormat, IReadOnlyDictionary<TagField, string>> tables = new()
        {
            [AudioFormat.M4a] = TagIoConst.MP4_ATOM_BY_FIELD,
            [AudioFormat.Flac] = TagIoConst.VORBIS_FIELD_BY_FIELD,
            [AudioFormat.Id3] = TagIoConst.ID3_FRAME_BY_FIELD,
        };

        foreach ((AudioFormat format, IReadOnlyDictionary<TagField, string> table) in tables)
        {
            foreach (TagField field in Enum.GetValues<TagField>())
            {
                Assert.Equal(TagFieldConst.IsSupported(format, field), table.ContainsKey(field));
            }
        }
    }

    /// <summary>
    /// 利用者に見せる拡張子の宣言（<c>AudioFormatConst</c>）と、実際に読み込む拡張子の対応表
    /// （<c>TagIoConst.FORMAT_BY_EXTENSION</c>）が一致することを確認する。
    ///
    /// **両者を同時に見られるのはこのテストだけである**（依存の向きは TagIo → Core）。
    /// 食い違うと、気づきの文面が実際には対象でない拡張子を名指しする。
    /// </summary>
    [Fact]
    public void AudioFormatConstMatchesExtensionTable()
    {
        foreach (AudioFormat format in Enum.GetValues<AudioFormat>())
        {
            IEnumerable<string> actual = TagIoConst.FORMAT_BY_EXTENSION
                .Where(pair => pair.Value == format)
                .Select(pair => pair.Key)
                .Order(StringComparer.Ordinal);

            IEnumerable<string> declared = AudioFormatConst.Extensions(format).Order(StringComparer.Ordinal);

            Assert.Equal(actual, declared);
        }
    }

    /// <summary>
    /// 指定した atom を持つ M4A を一時フォルダに書き出す。
    /// </summary>
    private string WriteM4a(
        IEnumerable<(string Name, int TypeFlag, byte[][] Values)> atoms,
        string fileName = "test.m4a")
    {
        string path = Path.Combine(_workDir, fileName);
        File.WriteAllBytes(path, MinimalAudioFileBuilder.BuildM4a(atoms));
        return path;
    }

    /// <summary>文字列を UTF-8 バイト列にする。</summary>
    private static byte[] Utf8(string value)
    {
        return Encoding.UTF8.GetBytes(value);
    }
}
