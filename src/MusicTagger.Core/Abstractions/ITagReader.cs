using MusicTagger.Core.Models;

namespace MusicTagger.Core.Abstractions;

/// <summary>
/// 音声ファイルからタグを読み取る。
///
/// 本インターフェイスを Core 側に置き、実装（どのライブラリを使うか）を MusicTagger.TagIo に閉じ込める。
/// TagLib# の更新が停滞している点への備えであり、差し替え可能にしておくためである
/// （docs/adr/0001-tag-io-library.md の「残るリスク」）。
/// </summary>
public interface ITagReader
{
    /// <summary>
    /// 1 ファイルのタグを読み取る。
    /// </summary>
    /// <param name="fullPath">対象ファイルの絶対パス。</param>
    /// <param name="relativePath">ライブラリルートからの相対パス。</param>
    /// <returns>読み取ったタグ。</returns>
    /// <exception cref="NotSupportedException">対応していない拡張子の場合。</exception>
    TrackTags Read(string fullPath, string relativePath);
}
