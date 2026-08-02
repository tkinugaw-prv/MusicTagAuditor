using System.Collections.Concurrent;
using System.Diagnostics;
using MusicTagger.Core.Abstractions;
using MusicTagger.Core.Models;

namespace MusicTagger.Core.Scanning;

/// <summary>
/// ライブラリを走査して全ファイルのタグを読み取る。
///
/// 1,000 ファイルを 10 秒以内、UI を止めないことが要件（docs/SPEC.md 11章）。
/// 1 件の読み取り失敗で全体を止めず、失敗一覧として持ち帰る。
/// </summary>
public sealed class LibraryScanner
{
    /// <summary>タグ読み取りの実装。</summary>
    private readonly ITagReader _tagReader;

    /// <summary>スキャン設定。</summary>
    private readonly ScanOptions _options;

    /// <summary>
    /// スキャナを初期化する。
    /// </summary>
    /// <param name="tagReader">タグ読み取りの実装。</param>
    /// <param name="options">スキャン設定。省略時は既定値。</param>
    public LibraryScanner(ITagReader tagReader, ScanOptions? options = null)
    {
        _tagReader = tagReader;
        _options = options ?? new ScanOptions();
    }

    /// <summary>
    /// ライブラリを走査し、対象ファイルのタグをすべて読み取る。
    /// </summary>
    /// <param name="libraryRoot">ライブラリのルートフォルダ。</param>
    /// <param name="progress">進捗の通知先。不要なら null。</param>
    /// <param name="cancellationToken">キャンセル用トークン。</param>
    /// <returns>スキャン結果。</returns>
    /// <exception cref="DirectoryNotFoundException">ルートフォルダが存在しない場合。</exception>
    public async Task<ScanResult> ScanAsync(
        string libraryRoot,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(libraryRoot))
        {
            throw new DirectoryNotFoundException($"ライブラリが見つかりません: {libraryRoot}");
        }

        Stopwatch stopwatch = Stopwatch.StartNew();

        string[] targets = [.. EnumerateTargetFiles(libraryRoot)];
        int total = targets.Length;
        int completed = 0;

        ConcurrentBag<TrackTags> tracks = [];
        ConcurrentBag<ScanFailure> failures = [];

        ParallelOptions parallelOptions = new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism > 0
                ? _options.MaxDegreeOfParallelism
                : Environment.ProcessorCount,
        };

        await Parallel.ForEachAsync(targets, parallelOptions, (fullPath, token) =>
        {
            token.ThrowIfCancellationRequested();

            string relativePath = Path.GetRelativePath(libraryRoot, fullPath);

            try
            {
                tracks.Add(_tagReader.Read(fullPath, relativePath));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(new ScanFailure(relativePath, $"{ex.GetType().Name}: {ex.Message}"));
            }

            progress?.Report(new ScanProgress(Interlocked.Increment(ref completed), total, relativePath));

            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);

        stopwatch.Stop();

        return new ScanResult(
            libraryRoot,
            [.. tracks.OrderBy(track => track.RelativePath, StringComparer.OrdinalIgnoreCase)],
            [.. failures.OrderBy(failure => failure.RelativePath, StringComparer.OrdinalIgnoreCase)],
            stopwatch.Elapsed);
    }

    /// <summary>
    /// 走査対象のファイルを列挙する。<c>backup_*</c> フォルダは除外する。
    /// </summary>
    /// <param name="libraryRoot">ライブラリのルートフォルダ。</param>
    /// <returns>対象ファイルの絶対パス。</returns>
    public IEnumerable<string> EnumerateTargetFiles(string libraryRoot)
    {
        EnumerationOptions enumerationOptions = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
        };

        foreach (string fullPath in Directory.EnumerateFiles(libraryRoot, "*", enumerationOptions))
        {
            if (!_options.Extensions.Contains(Path.GetExtension(fullPath), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (IsExcluded(libraryRoot, fullPath))
            {
                continue;
            }

            yield return fullPath;
        }
    }

    /// <summary>
    /// パスのどこかに除外対象のフォルダが含まれるかを判定する。
    /// ルート直下だけでなく、途中の階層にある backup_* も除外する。
    /// </summary>
    private static bool IsExcluded(string libraryRoot, string fullPath)
    {
        string relativePath = Path.GetRelativePath(libraryRoot, fullPath);
        string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // 最後の要素はファイル名なので除く。
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].StartsWith(ScanOptions.EXCLUDED_DIRECTORY_PREFIX, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
