namespace TagIoProbe;

/// <summary>
/// 検証対象ライブラリとは別の経路でファイルの中身を観測するための読み取り。
/// 「そのライブラリで書いて、そのライブラリで読めた」だけでは検証にならないため、
/// M4A は <see cref="Mp4AtomDumper"/>、それ以外はここで実フィールドを直接読む。
/// </summary>
internal static class NeutralReader
{
    /// <summary>
    /// FLAC / MP3 / AIFF について、指定フィールドに実際に格納されている値を列挙する。
    /// </summary>
    /// <param name="filePath">対象ファイル。</param>
    /// <param name="fieldName">Vorbis comment のフィールド名（ID3 では対応するフレームに読み替える）。</param>
    /// <returns>格納されている値。読めない場合は空。</returns>
    public static IReadOnlyList<string> ReadRawField(string filePath, string fieldName)
    {
        try
        {
            using TagLib.File file = TagLib.File.Create(filePath);

            if (file.GetTag(TagLib.TagTypes.Xiph) is TagLib.Ogg.XiphComment xiph)
            {
                return xiph.GetField(fieldName);
            }

            if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
            {
                return ReadId3Field(id3, fieldName);
            }

            return [];
        }
        catch (Exception ex)
        {
            return [$"<読み取り失敗: {ex.GetType().Name}>"];
        }
    }

    /// <summary>
    /// ID3v2 タグから、Vorbis comment 相当のフィールド名でフレーム値を取り出す。
    /// 対応は docs/TAGGING_POLICY.md 4.1 に従う。
    /// </summary>
    private static IReadOnlyList<string> ReadId3Field(TagLib.Id3v2.Tag id3, string fieldName)
    {
        string frameId = fieldName.ToUpperInvariant() switch
        {
            "ALBUMARTIST" => "TPE2",
            "CONDUCTOR" => "TPE3",
            "ARTIST" => "TPE1",
            "COMPOSER" => "TCOM",
            _ => string.Empty,
        };

        if (frameId.Length == 0)
        {
            return [];
        }

        List<string> values = [];
        foreach (TagLib.Id3v2.Frame frame in id3.GetFrames(new TagLib.ReadOnlyByteVector(frameId)))
        {
            if (frame is TagLib.Id3v2.TextInformationFrame textFrame)
            {
                values.AddRange(textFrame.Text);
            }
        }

        return values;
    }
}
