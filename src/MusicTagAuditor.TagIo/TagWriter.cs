using System.Globalization;
using MusicTagAuditor.Core.Abstractions;
using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.TagIo;

/// <summary>
/// フォーマットに応じて書き込み経路を振り分けるタグライター。
///
/// **M4A の指揮者は必ず <c>©con</c> に書く。** TagLib# の <c>Tag.Conductor</c> は <c>cond</c> に書き、
/// AIMP からは見えなくなる（docs/adr/0001-tag-io-library.md で実機確認済み）。
/// そのため M4A では汎用プロパティを使わず atom を明示する。
///
/// **既知の制約**: M4A では複数値を書き分けられない。TagLib# の <c>AppleTag.SetText</c> は
/// string[] を <c>"; "</c> で連結して 1 つの data ボックスに書くため、AIMP が分割した状態
/// （data ボックスが複数）を再現できない。FLAC / MP3 / AIFF には制約はない。
/// 復元後の読み戻し照合で不一致として検出されるので、黙って壊れることはない。
/// </summary>
public sealed class TagWriter : ITagWriter
{
    /// <summary>
    /// アプリ無しで復元するスクリプトが読み込むタグライブラリの場所。
    /// スナップショットと一緒に配置するために使う。
    /// </summary>
    /// <returns>TagLibSharp のアセンブリパス。単一ファイル発行等で取得できない場合は null。</returns>
    public static string? GetPortableLibraryPath()
    {
        string location = typeof(TagLib.File).Assembly.Location;

        return string.IsNullOrEmpty(location) ? null : location;
    }

    /// <inheritdoc />
    public void Write(string fullPath, IReadOnlyDictionary<TagField, IReadOnlyList<string>> fields)
    {
        string extension = Path.GetExtension(fullPath);

        if (!TagIoConst.FORMAT_BY_EXTENSION.TryGetValue(extension, out AudioFormat format))
        {
            throw new NotSupportedException($"対応していない拡張子です: {extension}");
        }

        using TagLib.File file = TagLib.File.Create(fullPath);

        switch (format)
        {
            case AudioFormat.M4a:
                WriteMp4(file, fields);
                break;

            case AudioFormat.Flac:
                WriteVorbis(file, fields);
                break;

            case AudioFormat.Id3:
                WriteId3(file, fields);
                break;

            default:
                throw new NotSupportedException($"対応していない格納形式です: {format}");
        }

        file.Save();
    }

    /// <summary>
    /// M4A に書き込む。atom を明示するため汎用の <c>Tag</c> プロパティは使わない。
    /// </summary>
    private static void WriteMp4(TagLib.File file, IReadOnlyDictionary<TagField, IReadOnlyList<string>> fields)
    {
        if (file.GetTag(TagLib.TagTypes.Apple, create: true) is not TagLib.Mpeg4.AppleTag appleTag)
        {
            throw new InvalidOperationException("AppleTag を取得できませんでした。");
        }

        foreach ((TagField field, IReadOnlyList<string> values) in fields)
        {
            switch (field)
            {
                case TagField.TrackNumber:
                    (appleTag.Track, appleTag.TrackCount) = ParseNumberPair(values);
                    break;

                case TagField.DiscNumber:
                    (appleTag.Disc, appleTag.DiscCount) = ParseNumberPair(values);
                    break;

                default:
                    WriteMp4Text(appleTag, field, values);
                    break;
            }
        }
    }

    /// <summary>
    /// M4A のテキスト atom を書き込む。値が無ければ atom ごと削除する。
    /// </summary>
    private static void WriteMp4Text(TagLib.Mpeg4.AppleTag appleTag, TagField field, IReadOnlyList<string> values)
    {
        if (!TagIoConst.MP4_ATOM_BY_FIELD.TryGetValue(field, out string? atomName))
        {
            return;
        }

        TagLib.ReadOnlyByteVector ident = new(EncodeAtomName(atomName));

        if (values.Count == 0)
        {
            appleTag.ClearData(ident);
            return;
        }

        // 1 つの値に ; が含まれていても分割しない。配列の要素数がそのまま data ボックスの数になる。
        appleTag.SetText(ident, [.. values]);
    }

    /// <summary>
    /// FLAC の Vorbis comment に書き込む。
    /// </summary>
    private static void WriteVorbis(TagLib.File file, IReadOnlyDictionary<TagField, IReadOnlyList<string>> fields)
    {
        if (file.GetTag(TagLib.TagTypes.Xiph, create: true) is not TagLib.Ogg.XiphComment xiph)
        {
            throw new InvalidOperationException("XiphComment を取得できませんでした。");
        }

        foreach ((TagField field, IReadOnlyList<string> values) in fields)
        {
            if (!TagIoConst.VORBIS_FIELD_BY_FIELD.TryGetValue(field, out string? fieldName))
            {
                continue;
            }

            if (values.Count == 0)
            {
                xiph.RemoveField(fieldName);
                continue;
            }

            xiph.SetField(fieldName, [.. values]);
        }
    }

    /// <summary>
    /// MP3 / AIFF の ID3v2 に書き込む。
    /// </summary>
    private static void WriteId3(TagLib.File file, IReadOnlyDictionary<TagField, IReadOnlyList<string>> fields)
    {
        if (file.GetTag(TagLib.TagTypes.Id3v2, create: true) is not TagLib.Id3v2.Tag id3)
        {
            throw new InvalidOperationException("ID3v2 タグを取得できませんでした。");
        }

        foreach ((TagField field, IReadOnlyList<string> values) in fields)
        {
            if (!TagIoConst.ID3_FRAME_BY_FIELD.TryGetValue(field, out string? frameId))
            {
                continue;
            }

            TagLib.ReadOnlyByteVector ident = new(frameId);

            if (values.Count == 0)
            {
                id3.RemoveFrames(ident);
                continue;
            }

            // string[] と StringCollection の両方の多重定義があるため、変数に取って型を確定させる。
            string[] text = [.. values];
            id3.SetTextFrame(ident, text);
        }
    }

    /// <summary>
    /// <c>番号/総数</c> の表記を数値の組に分解する。MP4 の <c>trkn</c> / <c>disk</c> 用。
    /// </summary>
    private static (uint Number, uint Count) ParseNumberPair(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return (0, 0);
        }

        string[] parts = values[0].Split('/', StringSplitOptions.TrimEntries);

        uint number = parts.Length > 0 && uint.TryParse(parts[0], CultureInfo.InvariantCulture, out uint parsedNumber)
            ? parsedNumber
            : 0;

        uint count = parts.Length > 1 && uint.TryParse(parts[1], CultureInfo.InvariantCulture, out uint parsedCount)
            ? parsedCount
            : 0;

        return (number, count);
    }

    /// <summary>
    /// atom 名を 4 バイトに変換する。© は 0xA9 にする。
    /// </summary>
    private static byte[] EncodeAtomName(string atomName)
    {
        byte[] bytes = new byte[4];
        int index = 0;

        foreach (char c in atomName)
        {
            bytes[index++] = c == '©' ? (byte)0xA9 : (byte)c;
        }

        if (index != 4)
        {
            throw new ArgumentException($"atom 名は 4 バイトである必要があります: {atomName}", nameof(atomName));
        }

        return bytes;
    }
}
