using System.Globalization;
using MusicTagAuditor.Core.Abstractions;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.TagIo.Mp4;

namespace MusicTagAuditor.TagIo;

/// <summary>
/// フォーマットに応じて読み取り経路を振り分けるタグリーダー。
///
/// M4A は自前の MP4 atom リーダーを使う。TagLib# の MP4 読み取りは値を <c>"; "</c> で分割するため、
/// 「1 値に <c>;</c> が含まれる状態」と「複数値に分割済みの状態」を区別できない
/// （docs/adr/0001-tag-io-library.md）。FLAC / MP3 / AIFF は TagLib# で問題ない。
/// </summary>
public sealed class TagReader : ITagReader
{
    /// <inheritdoc />
    public TrackTags Read(string fullPath, string relativePath)
    {
        string extension = Path.GetExtension(fullPath);

        if (!TagIoConst.FORMAT_BY_EXTENSION.TryGetValue(extension, out AudioFormat format))
        {
            throw new NotSupportedException($"対応していない拡張子です: {extension}");
        }

        return format switch
        {
            AudioFormat.M4a => ReadMp4(fullPath, relativePath),
            AudioFormat.Flac => ReadVorbis(fullPath, relativePath),
            AudioFormat.Id3 => ReadId3(fullPath, relativePath),
            _ => throw new NotSupportedException($"対応していない格納形式です: {format}"),
        };
    }

    /// <summary>
    /// M4A を自前の atom リーダーで読む。
    /// </summary>
    private static TrackTags ReadMp4(string fullPath, string relativePath)
    {
        IReadOnlyList<Mp4Atom> atoms = Mp4AtomReader.Read(fullPath);

        Dictionary<string, string[]> rawTags = [];
        foreach (Mp4Atom atom in atoms)
        {
            // 同名 atom が複数並ぶことがあるため、既出なら値を連結する。
            if (rawTags.TryGetValue(atom.Name, out string[]? existing))
            {
                rawTags[atom.Name] = [.. existing, .. atom.Values];
            }
            else
            {
                rawTags[atom.Name] = [.. atom.Values];
            }
        }

        List<KeyValuePair<TagField, IReadOnlyList<string>>> fields = [];
        foreach ((TagField field, string atomName) in TagIoConst.MP4_ATOM_BY_FIELD)
        {
            if (!rawTags.TryGetValue(atomName, out string[]? values))
            {
                continue;
            }

            IReadOnlyList<string> converted = field is TagField.TrackNumber or TagField.DiscNumber
                ? DecodeMp4NumberPair(values)
                : values;

            fields.Add(new KeyValuePair<TagField, IReadOnlyList<string>>(field, converted));
        }

        return new TrackTags
        {
            RelativePath = relativePath,
            FullPath = fullPath,
            Format = AudioFormat.M4a,
            Fields = TrackTags.BuildFields(fields),
            RawTags = rawTags,
        };
    }

    /// <summary>
    /// FLAC を Vorbis comment として読む。
    /// </summary>
    private static TrackTags ReadVorbis(string fullPath, string relativePath)
    {
        using TagLib.File file = TagLib.File.Create(fullPath);

        if (file.GetTag(TagLib.TagTypes.Xiph) is not TagLib.Ogg.XiphComment xiph)
        {
            return BuildEmpty(fullPath, relativePath, AudioFormat.Flac);
        }

        Dictionary<string, string[]> rawTags = [];
        foreach (string fieldName in xiph)
        {
            rawTags[fieldName] = xiph.GetField(fieldName);
        }

        List<KeyValuePair<TagField, IReadOnlyList<string>>> fields = [];
        foreach ((TagField field, string vorbisName) in TagIoConst.VORBIS_FIELD_BY_FIELD)
        {
            string[] values = xiph.GetField(vorbisName);
            if (values.Length > 0)
            {
                fields.Add(new KeyValuePair<TagField, IReadOnlyList<string>>(field, values));
            }
        }

        return new TrackTags
        {
            RelativePath = relativePath,
            FullPath = fullPath,
            Format = AudioFormat.Flac,
            Fields = TrackTags.BuildFields(fields),
            RawTags = rawTags,
        };
    }

    /// <summary>
    /// MP3 / AIFF を ID3v2 として読む。
    /// </summary>
    private static TrackTags ReadId3(string fullPath, string relativePath)
    {
        using TagLib.File file = TagLib.File.Create(fullPath);

        if (file.GetTag(TagLib.TagTypes.Id3v2) is not TagLib.Id3v2.Tag id3)
        {
            return BuildEmpty(fullPath, relativePath, AudioFormat.Id3);
        }

        Dictionary<string, string[]> rawTags = [];
        foreach (TagLib.Id3v2.Frame frame in id3.GetFrames())
        {
            string frameId = frame.FrameId.ToString();
            string[] values = frame is TagLib.Id3v2.TextInformationFrame textFrame
                ? textFrame.Text
                : [frame.ToString() ?? string.Empty];

            rawTags[frameId] = rawTags.TryGetValue(frameId, out string[]? existing)
                ? [.. existing, .. values]
                : values;
        }

        List<KeyValuePair<TagField, IReadOnlyList<string>>> fields = [];
        foreach ((TagField field, string frameId) in TagIoConst.ID3_FRAME_BY_FIELD)
        {
            if (rawTags.TryGetValue(frameId, out string[]? values) && values.Length > 0)
            {
                fields.Add(new KeyValuePair<TagField, IReadOnlyList<string>>(field, values));
            }
        }

        return new TrackTags
        {
            RelativePath = relativePath,
            FullPath = fullPath,
            Format = AudioFormat.Id3,
            Fields = TrackTags.BuildFields(fields),
            RawTags = rawTags,
        };
    }

    /// <summary>
    /// MP4 の <c>trkn</c> / <c>disk</c> のバイナリ値を <c>番号/総数</c> の表記に変換する。
    /// 構造は 2 バイトのパディングに続いて 番号(2) 総数(2)。
    /// </summary>
    private static IReadOnlyList<string> DecodeMp4NumberPair(IReadOnlyList<string> values)
    {
        List<string> decoded = [];

        foreach (string value in values)
        {
            if (!value.StartsWith(TagIoConst.BINARY_VALUE_PREFIX, StringComparison.Ordinal))
            {
                decoded.Add(value);
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromHexString(value[TagIoConst.BINARY_VALUE_PREFIX.Length..]);
            }
            catch (FormatException)
            {
                decoded.Add(value);
                continue;
            }

            if (bytes.Length < 4)
            {
                decoded.Add(value);
                continue;
            }

            int number = (bytes[2] << 8) | bytes[3];
            int total = bytes.Length >= 6 ? (bytes[4] << 8) | bytes[5] : 0;

            if (number == 0 && total == 0)
            {
                continue;
            }

            decoded.Add(total > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{number}/{total}")
                : number.ToString(CultureInfo.InvariantCulture));
        }

        return decoded;
    }

    /// <summary>
    /// タグ領域が存在しないファイル用の空の結果を作る。
    /// </summary>
    private static TrackTags BuildEmpty(string fullPath, string relativePath, AudioFormat format)
    {
        return new TrackTags
        {
            RelativePath = relativePath,
            FullPath = fullPath,
            Format = format,
            Fields = TrackTags.BuildFields([]),
            RawTags = new Dictionary<string, string[]>(),
        };
    }
}
