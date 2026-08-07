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
