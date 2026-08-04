using MusicTagAuditor.Core.Models;

namespace MusicTagAuditor.Core.Scanning;

/// <summary>
/// 読み取りに失敗したファイル 1 件。
/// 1 件の失敗でスキャン全体を止めないため、失敗も結果として持ち帰る（docs/SPEC.md 11章）。
/// </summary>
/// <param name="RelativePath">ライブラリルートからの相対パス。</param>
/// <param name="Message">失敗の内容。</param>
public sealed record ScanFailure(string RelativePath, string Message);

/// <summary>
/// ライブラリ 1 回分のスキャン結果。
/// </summary>
/// <param name="LibraryRoot">走査したライブラリのルート。</param>
/// <param name="Tracks">読み取れたファイルのタグ。相対パス順。</param>
/// <param name="Failures">読み取れなかったファイル。</param>
/// <param name="Elapsed">スキャンに要した時間。</param>
public sealed record ScanResult(
    string LibraryRoot,
    IReadOnlyList<TrackTags> Tracks,
    IReadOnlyList<ScanFailure> Failures,
    TimeSpan Elapsed);

/// <summary>
/// スキャンの進捗。
/// </summary>
/// <param name="Completed">読み取りを終えたファイル数。</param>
/// <param name="Total">対象ファイルの総数。</param>
/// <param name="CurrentRelativePath">直近に読み取ったファイル。</param>
public sealed record ScanProgress(int Completed, int Total, string CurrentRelativePath);
