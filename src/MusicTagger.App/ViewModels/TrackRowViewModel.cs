using System.IO;
using MusicTagger.Core.Models;

namespace MusicTagger.App.ViewModels;

/// <summary>
/// ファイル一覧タブの 1 行。読み取ったタグをそのまま表示する（段階 1 では編集しない）。
/// </summary>
/// <param name="tags">表示するタグ。</param>
public sealed class TrackRowViewModel(TrackTags tags)
{
    /// <summary>元になったタグ。</summary>
    public TrackTags Tags { get; } = tags;

    /// <summary>ライブラリルートからの相対パス。</summary>
    public string RelativePath => Tags.RelativePath;

    /// <summary>相対パスのうちフォルダ部分。ツリーでの絞り込みに使う。</summary>
    public string FolderPath => Path.GetDirectoryName(Tags.RelativePath) ?? string.Empty;

    /// <summary>ファイル名。</summary>
    public string FileName => Path.GetFileName(Tags.RelativePath);

    /// <summary>タグの格納形式。</summary>
    public string Format => Tags.Format.ToString();

    /// <summary>曲名。</summary>
    public string? Title => Tags.Title;

    /// <summary>その録音の主役。</summary>
    public string? Artist => Tags.Artist;

    /// <summary>演奏団体。</summary>
    public string? AlbumArtist => Tags.AlbumArtist;

    /// <summary>作曲家。</summary>
    public string? Composer => Tags.Composer;

    /// <summary>指揮者。</summary>
    public string? Conductor => Tags.Conductor;

    /// <summary>アルバム名。</summary>
    public string? Album => Tags.Album;

    /// <summary>ジャンル。</summary>
    public string? Genre => Tags.Genre;

    /// <summary>録音年。</summary>
    public string? Date => Tags.Date;

    /// <summary>トラック番号。</summary>
    public string? TrackNumber => Tags.TrackNumber;

    /// <summary>ディスク番号。</summary>
    public string? DiscNumber => Tags.DiscNumber;

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
}
