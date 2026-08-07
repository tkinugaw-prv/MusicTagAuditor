using MusicTagAuditor.Core.Abstractions;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;

namespace MusicTagAuditor.Core.Tests.Scanning;

/// <summary>
/// ライブラリスキャナのテスト。実ファイルの読み取りは <see cref="FakeTagReader"/> に置き換え、
/// 走査・除外・失敗の扱い・進捗・キャンセルの挙動だけを検証する。
/// </summary>
public sealed class LibraryScannerTests : IDisposable
{
    /// <summary>テスト用のフォルダツリーを作る場所。</summary>
    private readonly string _root;

    /// <summary>
    /// テスト用の一時フォルダを用意する。
    /// </summary>
    public LibraryScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MusicTagAuditor.tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// 一時フォルダを削除する。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// 対象拡張子のファイルだけを読み取ることを確認する。
    /// </summary>
    [Fact]
    public async Task ScansOnlyTargetExtensions()
    {
        CreateFile("ブルックナー 8/01.m4a");
        CreateFile("ブルックナー 8/02.flac");
        CreateFile("ブルックナー 8/cover.jpg");
        CreateFile("ブルックナー 8/notes.txt");

        ScanResult result = await new LibraryScanner(new FakeTagReader()).ScanAsync(_root);

        Assert.Equal(2, result.Tracks.Count);
        Assert.Empty(result.Failures);
    }

    /// <summary>
    /// <c>backup_*</c> フォルダを除外することを確認する。
    /// 実ライブラリの backup フォルダには音源の複製が入っており、数えると二重になる。
    /// </summary>
    [Fact]
    public async Task ExcludesBackupDirectories()
    {
        CreateFile("ブルックナー 8/01.m4a");
        CreateFile("backup_20260802144324/01.m4a");
        CreateFile("backup_20260802145251/TAGTEST 皇帝 1st Mov.m4a");

        ScanResult result = await new LibraryScanner(new FakeTagReader()).ScanAsync(_root);

        TrackTags track = Assert.Single(result.Tracks);
        Assert.Equal(Path.Combine("ブルックナー 8", "01.m4a"), track.RelativePath);
    }

    /// <summary>
    /// 途中の階層にある <c>backup_*</c> も除外することを確認する。
    /// </summary>
    [Fact]
    public async Task ExcludesNestedBackupDirectories()
    {
        CreateFile("シベリウス/backup_20260801000000/01.flac");
        CreateFile("シベリウス/01.flac");

        ScanResult result = await new LibraryScanner(new FakeTagReader()).ScanAsync(_root);

        Assert.Single(result.Tracks);
    }

    /// <summary>
    /// バックアップ先をライブラリ配下の任意フォルダに設定しても、
    /// その中の音声ファイルがスキャン対象に入らないことを確認する。
    ///
    /// 保存先を選べるようになったため、<c>ライブラリ/_バックアップ/backup_*/…</c> という
    /// 形が生まれる。除外は中間階層まで見ているのでこの形でも効く。
    /// </summary>
    [Fact]
    public async Task ExcludesBackupDirectoriesUnderCustomBackupRoot()
    {
        CreateFile("ブルックナー 8/01.m4a");
        CreateFile("_バックアップ置き場/backup_20260803031500/01.m4a");

        ScanResult result = await new LibraryScanner(new FakeTagReader()).ScanAsync(_root);

        TrackTags track = Assert.Single(result.Tracks);
        Assert.Equal(Path.Combine("ブルックナー 8", "01.m4a"), track.RelativePath);
    }

    /// <summary>
    /// 1 件の読み取り失敗で全体を止めず、失敗一覧に残すことを確認する（docs/SPEC.md 11章）。
    /// </summary>
    [Fact]
    public async Task ContinuesAfterReadFailure()
    {
        CreateFile("ok1.m4a");
        CreateFile("broken.m4a");
        CreateFile("ok2.m4a");

        FakeTagReader reader = new() { FailingFileName = "broken.m4a" };

        ScanResult result = await new LibraryScanner(reader).ScanAsync(_root);

        Assert.Equal(2, result.Tracks.Count);
        ScanFailure failure = Assert.Single(result.Failures);
        Assert.Equal("broken.m4a", failure.RelativePath);
        Assert.Contains("読み取り失敗", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 進捗が全件分通知され、総数が正しいことを確認する。
    /// </summary>
    [Fact]
    public async Task ReportsProgressForEveryFile()
    {
        CreateFile("01.m4a");
        CreateFile("02.m4a");
        CreateFile("03.m4a");

        // Progress<T> は同期コンテキストへ非同期に流すため、通知の到着時刻も順序も保証されない。
        // ここで確かめたいのは「スキャナが全件分を通知するか」なので、同期的に記録する実装を渡す。
        RecordingProgress progress = new();

        await new LibraryScanner(new FakeTagReader(), new ScanOptions { MaxDegreeOfParallelism = 1 })
            .ScanAsync(_root, progress);

        IReadOnlyList<ScanProgress> reports = progress.Reports;

        Assert.All(reports, report => Assert.Equal(3, report.Total));
        Assert.Equal([1, 2, 3], reports.Select(report => report.Completed).Order());
    }

    /// <summary>
    /// キャンセルが効くことを確認する。
    /// </summary>
    [Fact]
    public async Task ThrowsWhenCancelled()
    {
        for (int i = 0; i < 50; i++)
        {
            CreateFile($"{i:D3}.m4a");
        }

        using CancellationTokenSource cts = new();
        FakeTagReader reader = new() { OnRead = () => cts.Cancel() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new LibraryScanner(reader, new ScanOptions { MaxDegreeOfParallelism = 1 })
                .ScanAsync(_root, progress: null, cts.Token));
    }

    /// <summary>
    /// 存在しないフォルダを指定した場合に例外になることを確認する。
    /// </summary>
    [Fact]
    public async Task ThrowsWhenLibraryRootMissing()
    {
        string missing = Path.Combine(_root, "存在しない");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => new LibraryScanner(new FakeTagReader()).ScanAsync(missing));
    }

    /// <summary>
    /// 結果が相対パス順に並ぶことを確認する。並列読み取りでも順序が安定する必要がある。
    /// </summary>
    [Fact]
    public async Task OrdersTracksByRelativePath()
    {
        CreateFile("c/01.m4a");
        CreateFile("a/01.m4a");
        CreateFile("b/01.m4a");

        ScanResult result = await new LibraryScanner(new FakeTagReader()).ScanAsync(_root);

        Assert.Equal(
            [Path.Combine("a", "01.m4a"), Path.Combine("b", "01.m4a"), Path.Combine("c", "01.m4a")],
            result.Tracks.Select(track => track.RelativePath));
    }

    /// <summary>
    /// 進捗を同期的に記録する。並列読み取りから呼ばれるためロックで保護する。
    /// </summary>
    private sealed class RecordingProgress : IProgress<ScanProgress>
    {
        /// <summary>記録した通知。</summary>
        private readonly List<ScanProgress> _reports = [];

        /// <summary>排他用。</summary>
        private readonly Lock _gate = new();

        /// <summary>記録した通知のコピー。</summary>
        public IReadOnlyList<ScanProgress> Reports
        {
            get
            {
                lock (_gate)
                {
                    return [.. _reports];
                }
            }
        }

        /// <inheritdoc />
        public void Report(ScanProgress value)
        {
            lock (_gate)
            {
                _reports.Add(value);
            }
        }
    }

    /// <summary>
    /// 空のファイルを作る。中身は <see cref="FakeTagReader"/> が読まないため不要。
    /// </summary>
    private void CreateFile(string relativePath)
    {
        string fullPath = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, []);
    }

    /// <summary>
    /// 実ファイルを読まないタグリーダー。スキャナの挙動だけを検証するために使う。
    /// </summary>
    private sealed class FakeTagReader : ITagReader
    {
        /// <summary>この名前のファイルを読もうとしたら失敗させる。</summary>
        public string? FailingFileName { get; init; }

        /// <summary>読み取りのたびに呼ぶ処理。キャンセルの発火に使う。</summary>
        public Action? OnRead { get; init; }

        /// <inheritdoc />
        public TrackTags Read(string fullPath, string relativePath)
        {
            OnRead?.Invoke();

            if (FailingFileName is not null && Path.GetFileName(fullPath) == FailingFileName)
            {
                throw new InvalidDataException("読み取り失敗（テスト用）");
            }

            return new TrackTags
            {
                RelativePath = relativePath,
                FullPath = fullPath,
                Format = AudioFormat.M4a,
                Fields = TrackTags.BuildFields([]),
                RawTags = new Dictionary<string, string[]>(),
            };
        }
    }
}
