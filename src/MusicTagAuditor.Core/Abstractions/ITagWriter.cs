using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Abstractions;

/// <summary>
/// 音声ファイルへタグを書き込む。
///
/// 指定されたフィールドだけを書き換え、それ以外のタグ（<c>iTunNORM</c>、カバーアート等）は
/// そのまま残すこと。<c>RawTags</c> を失わないための前提である（docs/SPEC.md 10章）。
/// </summary>
public interface ITagWriter
{
    /// <summary>
    /// 指定したフィールドを書き込む。
    /// </summary>
    /// <param name="fullPath">対象ファイルの絶対パス。</param>
    /// <param name="fields">
    /// 書き込むフィールドと値。値が空のフィールドはタグを削除する。
    /// 1 つの値に <c>;</c> が含まれていても分割してはならない（docs/TAGGING_POLICY.md 3.4）。
    /// </param>
    /// <exception cref="NotSupportedException">対応していない拡張子の場合。</exception>
    void Write(string fullPath, IReadOnlyDictionary<TagField, IReadOnlyList<string>> fields);
}
