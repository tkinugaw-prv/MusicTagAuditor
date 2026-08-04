using System.Diagnostics;
using MusicTagAuditor.Core.Backup;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;
using MusicTagAuditor.TagIo.Tests.Fixtures;

namespace MusicTagAuditor.TagIo.Tests.Integration;

/// <summary>
/// PowerShell が使える環境でのみ実行するテスト。
/// </summary>
public sealed class PowerShellFactAttribute : FactAttribute
{
    /// <summary>
    /// pwsh の有無を見てスキップ理由を設定する。
    /// </summary>
    public PowerShellFactAttribute()
    {
        if (FindPowerShell() is null)
        {
            Skip = "pwsh が見つからないためスキップした";
        }
    }

    /// <summary>
    /// PATH から pwsh を探す。
    /// </summary>
    /// <returns>実行ファイルのパス。見つからなければ null。</returns>
    public static string? FindPowerShell()
    {
        string executable = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        string? paths = Environment.GetEnvironmentVariable("PATH");

        if (paths is null)
        {
            return null;
        }

        foreach (string directory in paths.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            string candidate = Path.Combine(directory, executable);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>
/// 同梱する復元用 PowerShell が、アプリ無しで実際にタグを巻き戻せることを確認する。
///
/// **これは安全網そのもののテストである。** 書き込み機能（段階 4）より先に、
/// 復元手段が動くことを確認しておくのが段階 2 の目的（docs/SPEC.md 12章）。
/// </summary>
public sealed class RestoreScriptTests : IDisposable
{
    /// <summary>スクリプトの出力をテストログへ流すための出力先。</summary>
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    /// <summary>テスト用ライブラリのルート。</summary>
    private readonly string _root;

    /// <summary>タグ書き込み。</summary>
    private readonly TagWriter _writer = new();

    /// <summary>タグ読み取り。</summary>
    private readonly TagReader _reader = new();

    /// <summary>
    /// テスト用の一時ライブラリを用意する。
    /// </summary>
    /// <param name="output">テスト出力。スクリプトの標準出力を流す。</param>
    public RestoreScriptTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        _root = Path.Combine(Path.GetTempPath(), "musicTagger.tests", Guid.NewGuid().ToString("N"));
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
    /// スナップショット取得 → タグを書き換え → スクリプトで復元、が全フォーマットで通ることを確認する。
    /// </summary>
    [PowerShellFact]
    public async Task RestoresTagsWithoutTheApplication()
    {
        Dictionary<string, string> originalConductors = new(StringComparer.Ordinal)
        {
            ["01.m4a"] = "Günter Wand",
            ["02.flac"] = "Yevgeny Mravinsky",
            ["03.mp3"] = "Karl Böhm",
            ["04.aif"] = "Herbert von Karajan",
        };

        foreach ((string fileName, string conductor) in originalConductors)
        {
            CreateFile(fileName);
            _writer.Write(Path.Combine(_root, fileName), new Dictionary<TagField, IReadOnlyList<string>>
            {
                [TagField.Conductor] = [conductor],
                [TagField.Composer] = ["Anton Bruckner"],
                [TagField.Genre] = ["Classic"],
            });
        }

        string backupDirectory = await CreateSnapshotAsync();

        // タグを壊す。指揮者を別人にし、作曲家を消す。
        foreach (string fileName in originalConductors.Keys)
        {
            _writer.Write(Path.Combine(_root, fileName), new Dictionary<TagField, IReadOnlyList<string>>
            {
                [TagField.Conductor] = ["まちがった指揮者"],
                [TagField.Composer] = [],
            });
        }

        (int exitCode, string output) = RunRestoreScript(backupDirectory, dryRun: false);

        Assert.Equal(0, exitCode);
        Assert.Contains("復元 4 件", output, StringComparison.Ordinal);

        foreach ((string fileName, string conductor) in originalConductors)
        {
            TrackTags restored = _reader.Read(Path.Combine(_root, fileName), fileName);

            Assert.Equal(conductor, restored.Conductor);
            Assert.Equal("Anton Bruckner", restored.Composer);
            Assert.Equal("Classic", restored.Genre);
        }
    }

    /// <summary>
    /// M4A の指揮者がスクリプト経由でも <c>©con</c> に書かれることを確認する。
    /// スクリプト側で <c>Tag.Conductor</c> を使うと AIMP から見えなくなる。
    /// </summary>
    [PowerShellFact]
    public async Task ScriptWritesM4aConductorToCopyrightConAtom()
    {
        CreateFile("01.m4a");
        string fullPath = Path.Combine(_root, "01.m4a");

        _writer.Write(fullPath, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Conductor] = ["Sergiu Celibidache"],
        });

        string backupDirectory = await CreateSnapshotAsync();

        _writer.Write(fullPath, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Conductor] = [],
        });

        RunRestoreScript(backupDirectory, dryRun: false);

        IReadOnlyList<Mp4.Mp4Atom> atoms = Mp4.Mp4AtomReader.Read(fullPath);

        Mp4.Mp4Atom conductor = Assert.Single(atoms, atom => atom.Name == TagIoConst.ATOM_CONDUCTOR);
        Assert.Equal(["Sergiu Celibidache"], conductor.Values);
        Assert.DoesNotContain(atoms, atom => atom.Name == TagIoConst.ATOM_CONDUCTOR_WRONG);
    }

    /// <summary>
    /// <c>-DryRun</c> では差分を表示するだけで書き込まないことを確認する。
    /// 「何が戻るのかを確認できること」が要件（docs/SPEC.md 8.3）。
    /// </summary>
    [PowerShellFact]
    public async Task DryRunShowsDifferencesWithoutWriting()
    {
        CreateFile("01.m4a");
        string fullPath = Path.Combine(_root, "01.m4a");

        _writer.Write(fullPath, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Composer] = ["Anton Bruckner"],
        });

        string backupDirectory = await CreateSnapshotAsync();

        _writer.Write(fullPath, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Composer] = ["Btuckner"],
        });

        (int exitCode, string output) = RunRestoreScript(backupDirectory, dryRun: true);

        Assert.Equal(0, exitCode);
        Assert.Contains("Btuckner", output, StringComparison.Ordinal);
        Assert.Contains("Anton Bruckner", output, StringComparison.Ordinal);
        Assert.Contains("書き込んでいません", output, StringComparison.Ordinal);

        // 書き換わっていないこと。
        Assert.Equal("Btuckner", _reader.Read(fullPath, "01.m4a").Composer);
    }

    /// <summary>
    /// <c>;</c> を含む配役情報が、スクリプト経由でも 1 値のまま復元されることを確認する。
    /// docs/TAGGING_POLICY.md 2.3 の保護対象がこれに当たる。
    /// </summary>
    [PowerShellFact]
    public async Task RestoresProtectedAlbumArtistWithoutSplitting()
    {
        const string PROTECTED_VALUE = "Kommerchor Stuttgart(Chorus); Karl Münchinger; Stuttgarter Kammerorchester";

        CreateFile("01.m4a");
        string fullPath = Path.Combine(_root, "01.m4a");

        _writer.Write(fullPath, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.AlbumArtist] = [PROTECTED_VALUE],
        });

        string backupDirectory = await CreateSnapshotAsync();

        _writer.Write(fullPath, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.AlbumArtist] = ["Stuttgarter Kammerorchester"],
        });

        RunRestoreScript(backupDirectory, dryRun: false);

        TrackTags restored = _reader.Read(fullPath, "01.m4a");

        Assert.Equal([PROTECTED_VALUE], restored.GetValues(TagField.AlbumArtist));
        Assert.False(restored.HasMultipleValues(TagField.AlbumArtist));
    }

    /// <summary>
    /// 現在のライブラリからスナップショットを取る。
    /// </summary>
    private async Task<string> CreateSnapshotAsync()
    {
        ScanResult scan = await new LibraryScanner(_reader).ScanAsync(_root);

        return new SnapshotService().Create(
            scan,
            SnapshotReason.Manual,
            portableLibraryPath: TagWriter.GetPortableLibraryPath());
    }

    /// <summary>
    /// 復元スクリプトを実行する。
    /// </summary>
    private (int ExitCode, string Output) RunRestoreScript(string backupDirectory, bool dryRun)
    {
        string scriptPath = Path.Combine(backupDirectory, BackupConst.RESTORE_SCRIPT_FILE_NAME);

        Assert.True(File.Exists(scriptPath), $"復元スクリプトが同梱されていません: {scriptPath}");

        ProcessStartInfo startInfo = new()
        {
            FileName = PowerShellFactAttribute.FindPowerShell()!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,

            // スクリプトは UTF-8 で出力する。既定のコードページのままだと日本語が化ける。
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        if (dryRun)
        {
            startInfo.ArgumentList.Add("-DryRun");
        }

        using Process process = Process.Start(startInfo)!;

        string result = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        _output.WriteLine(result);

        return (process.ExitCode, result);
    }

    /// <summary>
    /// 指定した拡張子の空検体をライブラリ直下に作る。
    /// </summary>
    private void CreateFile(string fileName)
    {
        byte[] bytes = Path.GetExtension(fileName) switch
        {
            ".m4a" => MinimalAudioFileBuilder.BuildM4a([]),
            ".flac" => MinimalAudioFileBuilder.BuildFlac(),
            ".mp3" => MinimalAudioFileBuilder.BuildMp3(),
            ".aif" => MinimalAudioFileBuilder.BuildAiff(),
            _ => throw new ArgumentException($"未対応の拡張子です: {fileName}", nameof(fileName)),
        };

        File.WriteAllBytes(Path.Combine(_root, fileName), bytes);
    }
}
