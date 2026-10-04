using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
    /// <c>comment</c> がスクリプト経由でも往復することを確認する（M4A / FLAC）。
    ///
    /// 併せて、**ID3 の <c>COMM</c> に触らないこと**を見る。スクリプトの対応表に
    /// <c>COMM</c> を足すと、iTunes が入れた <c>iTunNORM</c> 等を巻き添えで消す
    /// （docs/TAGGING_POLICY.md 4.4）。本体と同じ判断をスクリプト側でも固定する。
    /// </summary>
    [PowerShellFact]
    public async Task RestoresCommentWithoutTouchingId3CommentFrames()
    {
        CreateFile("01.m4a");
        CreateFile("02.flac");
        CreateFile("03.aif");

        foreach (string fileName in new[] { "01.m4a", "02.flac" })
        {
            _writer.Write(Path.Combine(_root, fileName), new Dictionary<TagField, IReadOnlyList<string>>
            {
                [TagField.Comment] = ["ハース版"],
            });
        }

        string aifPath = Path.Combine(_root, "03.aif");
        using (TagLib.File file = TagLib.File.Create(aifPath))
        {
            TagLib.Id3v2.Tag id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, create: true);
            id3.AddFrame(new TagLib.Id3v2.CommentsFrame("iTunNORM", "eng") { Text = " 000001A5 00000174" });
            file.Save();
        }

        string backupDirectory = await CreateSnapshotAsync();

        foreach (string fileName in new[] { "01.m4a", "02.flac" })
        {
            _writer.Write(Path.Combine(_root, fileName), new Dictionary<TagField, IReadOnlyList<string>>
            {
                [TagField.Comment] = ["ノヴァーク版"],
            });
        }

        (int exitCode, _) = RunRestoreScript(backupDirectory, dryRun: false);

        Assert.Equal(0, exitCode);
        Assert.Equal("ハース版", _reader.Read(Path.Combine(_root, "01.m4a"), "01.m4a").Comment);
        Assert.Equal("ハース版", _reader.Read(Path.Combine(_root, "02.flac"), "02.flac").Comment);

        using TagLib.File restored = TagLib.File.Create(aifPath);
        TagLib.Id3v2.Tag restoredId3 = (TagLib.Id3v2.Tag)restored.GetTag(TagLib.TagTypes.Id3v2);

        TagLib.Id3v2.CommentsFrame frame = Assert.Single(
            restoredId3.GetFrames().OfType<TagLib.Id3v2.CommentsFrame>());

        Assert.Equal("iTunNORM", frame.Description);
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
    /// スナップショットの後に入った値を、スクリプトが空に戻すことを確認する。
    ///
    /// スナップショットは空のフィールドをキーごと省く。以前のスクリプトはキーのあるフィールドしか
    /// 回さなかったため、genre の補完や指揮者の手編集で埋めた値が残り、
    /// アプリの復元（<see cref="RestoreService.BuildPlan"/>）と結果が食い違っていた。
    /// </summary>
    [PowerShellFact]
    public async Task RemovesValuesAddedAfterTheSnapshot()
    {
        string[] fileNames = ["01.m4a", "02.flac", "03.mp3", "04.aif"];

        foreach (string fileName in fileNames)
        {
            CreateFile(fileName);
            _writer.Write(Path.Combine(_root, fileName), new Dictionary<TagField, IReadOnlyList<string>>
            {
                [TagField.Composer] = ["Anton Bruckner"],
            });
        }

        string backupDirectory = await CreateSnapshotAsync();

        foreach (string fileName in fileNames)
        {
            Dictionary<TagField, IReadOnlyList<string>> added = new()
            {
                [TagField.Conductor] = ["Günter Wand"],
                [TagField.Genre] = ["Classic"],
                [TagField.TrackNumber] = ["3/9"],
            };

            // ID3 は comment を扱わない（docs/TAGGING_POLICY.md 4.4）。
            if (Path.GetExtension(fileName) is ".m4a" or ".flac")
            {
                added[TagField.Comment] = ["ハース版"];
            }

            _writer.Write(Path.Combine(_root, fileName), added);
        }

        (int exitCode, string output) = RunRestoreScript(backupDirectory, dryRun: false);

        Assert.Equal(0, exitCode);
        Assert.Contains("復元 4 件", output, StringComparison.Ordinal);

        foreach (string fileName in fileNames)
        {
            TrackTags restored = _reader.Read(Path.Combine(_root, fileName), fileName);

            Assert.Equal("Anton Bruckner", restored.Composer);
            Assert.Null(restored.Conductor);
            Assert.Null(restored.Genre);
            Assert.Null(restored.TrackNumber);
            Assert.Null(restored.Comment);
        }
    }

    /// <summary>
    /// そのフィールドを記録していなかった版のスナップショットからは、今入っている値を消さないことを確認する。
    /// 版 1 は comment を記録していない。キーが無いのは「空だった」からとは限らない。
    /// </summary>
    [PowerShellFact]
    public async Task KeepsFieldsTheOldSchemaDidNotRecord()
    {
        CreateFile("01.m4a");
        string fullPath = Path.Combine(_root, "01.m4a");

        _writer.Write(fullPath, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Composer] = ["Anton Bruckner"],
        });

        string backupDirectory = await CreateSnapshotAsync();
        RewriteSchemaVersion(backupDirectory, BackupConst.SCHEMA_VERSION_WITH_COMMENT - 1);

        _writer.Write(fullPath, new Dictionary<TagField, IReadOnlyList<string>>
        {
            [TagField.Comment] = ["ハース版"],
            [TagField.Genre] = ["Classic"],
        });

        (int exitCode, _) = RunRestoreScript(backupDirectory, dryRun: false);

        Assert.Equal(0, exitCode);

        TrackTags restored = _reader.Read(fullPath, "01.m4a");

        Assert.Equal("ハース版", restored.Comment);

        // 版 1 でも記録していたフィールドは、これまでどおり空に戻る。
        Assert.Null(restored.Genre);
    }

    /// <summary>
    /// スクリプトが持つフィールドの表が、アプリの定義と一致することを確認する。
    ///
    /// スクリプトは単体で動く必要があるので、<see cref="TagField"/> と
    /// <see cref="BackupConst.IsFieldRecorded"/> の表を写して持っている。
    /// フィールドや版を足したときにスクリプトだけ取り残されると、そのフィールドは復元されないか、
    /// 古い版から戻したときに今の値を消す。pwsh が無くても検出できるよう、本文を読んで比べる。
    /// </summary>
    [Fact]
    public void ScriptFieldTablesMatchTheApplication()
    {
        string script = ReadEmbeddedScript();

        Match fieldBlock = Regex.Match(script, @"\$ALL_FIELDS = @\((?<body>[^)]*)\)");
        Assert.True(fieldBlock.Success, "スクリプトに $ALL_FIELDS が見つかりません");

        string[] scriptFields = [.. Regex.Matches(fieldBlock.Groups["body"].Value, "'(?<name>[A-Za-z]+)'")
            .Select(match => match.Groups["name"].Value)];

        Assert.Equal(Enum.GetNames<TagField>(), scriptFields);

        Match versionBlock = Regex.Match(script, @"\$FIRST_VERSION_BY_FIELD = @\{(?<body>[^}]*)\}");
        Assert.True(versionBlock.Success, "スクリプトに $FIRST_VERSION_BY_FIELD が見つかりません");

        Dictionary<string, int> scriptVersions = Regex.Matches(versionBlock.Groups["body"].Value, @"(?<name>[A-Za-z]+)\s*=\s*(?<version>\d+)")
            .ToDictionary(
                match => match.Groups["name"].Value,
                match => int.Parse(match.Groups["version"].Value, CultureInfo.InvariantCulture));

        // 本体の表は非公開なので、版ごとの判定結果で比べる。
        foreach (TagField field in Enum.GetValues<TagField>())
        {
            for (int version = 1; version <= BackupConst.SCHEMA_VERSION; version++)
            {
                bool scriptRecorded = !scriptVersions.TryGetValue(field.ToString(), out int firstVersion)
                    || version >= firstVersion;

                Assert.True(
                    BackupConst.IsFieldRecorded(version, field) == scriptRecorded,
                    $"{field} の版 {version} での扱いがスクリプトと本体で食い違っています");
            }
        }
    }

    /// <summary>
    /// 本体に埋め込まれた復元スクリプトを読む。バックアップへ書き出されるのと同じ本文。
    /// </summary>
    private static string ReadEmbeddedScript()
    {
        Assembly assembly = typeof(SnapshotService).Assembly;

        string resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(BackupConst.RESTORE_SCRIPT_FILE_NAME, StringComparison.Ordinal));

        using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
        using StreamReader reader = new(stream);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// スナップショットのスキーマ版を書き換える。古い版で取ったバックアップを再現するために使う。
    /// </summary>
    private static void RewriteSchemaVersion(string backupDirectory, int version)
    {
        string snapshotPath = Path.Combine(backupDirectory, BackupConst.SNAPSHOT_FILE_NAME);

        JsonNode snapshot = JsonNode.Parse(File.ReadAllText(snapshotPath))!;
        snapshot["version"] = version;
        File.WriteAllText(snapshotPath, snapshot.ToJsonString());
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
