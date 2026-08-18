using System.IO;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Abstractions;
using MusicTagAuditor.Core.Applying;
using MusicTagAuditor.Core.Backup;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Models;
using MusicTagAuditor.Core.Scanning;
using MusicTagAuditor.Core.Settings;

namespace MusicTagAuditor.App.Tests.ViewModels;

/// <summary>
/// ライブラリ・バックアップ先のパス欄の折り畳み（docs/SPEC.md 5.1）のテスト。
///
/// 折り畳み自体は XAML の <c>Visibility</c> なので自動テストで見られない。
/// ここで守るのは、その土台になる 2 点だけである。
/// <list type="bullet">
///   <item>畳んだ状態が settings.json に残り、次の起動で戻ること</item>
///   <item>畳んでいる間に見せる要約が、開いているライブラリに追従すること</item>
/// </list>
/// </summary>
public sealed class MainViewModelPathPanelTests : IDisposable
{
    /// <summary>テスト用の作業ディレクトリ。ライブラリ・設定・辞書をここに置く。</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MusicTagAuditor-vm-" + Guid.NewGuid().ToString("N"));

    /// <summary>ライブラリを 1 件用意する。スキャンが空でも折り畳みの検証には足りる。</summary>
    public MainViewModelPathPanelTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "library", "ブルックナー"));
        File.WriteAllBytes(Path.Combine(_root, "library", "ブルックナー", "01 Allegro.m4a"), []);
    }

    /// <summary>既定では展開している。従来の見た目を変えないため。</summary>
    [Fact]
    public void 既定では畳まれていない()
    {
        MainViewModel viewModel = CreateViewModel(out _);

        Assert.False(viewModel.ArePathsCollapsed);
    }

    /// <summary>
    /// 畳むと設定に残ることを確認する。**別インスタンスで読み直して見る。**
    /// 同じストアの <c>Current</c> を見るだけでは、ファイルに書けているか分からない。
    /// </summary>
    [Fact]
    public void 畳むと設定ファイルに残る()
    {
        MainViewModel viewModel = CreateViewModel(out string settingsDirectory);

        viewModel.ArePathsCollapsed = true;

        Assert.True(new AppSettingsStore(settingsDirectory).Current.PathsCollapsed);
    }

    /// <summary>開き直したときも設定に反映されることを確認する。片道だけでは戻せない。</summary>
    [Fact]
    public void 開き直すと設定も戻る()
    {
        MainViewModel viewModel = CreateViewModel(out string settingsDirectory);

        viewModel.ArePathsCollapsed = true;
        viewModel.ArePathsCollapsed = false;

        Assert.False(new AppSettingsStore(settingsDirectory).Current.PathsCollapsed);
    }

    /// <summary>畳んだ設定で起動すると、畳んだ状態で始まることを確認する。</summary>
    [Fact]
    public void 畳んだ設定で起動すると畳んだまま始まる()
    {
        string settingsDirectory = Path.Combine(_root, "settings");
        Directory.CreateDirectory(settingsDirectory);
        new AppSettingsStore(settingsDirectory).Save(AppSettings.Default with { PathsCollapsed = true });

        MainViewModel viewModel = CreateViewModel(out _);

        Assert.True(viewModel.ArePathsCollapsed);
    }

    /// <summary>
    /// 設定から復元しただけでは書き戻さないことを確認する。
    /// 構築のたびに保存が走ると、設定ファイルの更新時刻が起動しただけで動く。
    /// </summary>
    [Fact]
    public void 起動しただけでは設定を書き直さない()
    {
        string settingsDirectory = Path.Combine(_root, "settings");
        Directory.CreateDirectory(settingsDirectory);
        new AppSettingsStore(settingsDirectory).Save(AppSettings.Default with { PathsCollapsed = true });

        string settingsPath = AppSettingsStore.GetSettingsPath(settingsDirectory);
        DateTime writtenAt = File.GetLastWriteTimeUtc(settingsPath);

        CreateViewModel(out _);

        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(settingsPath));
    }

    /// <summary>ライブラリを開いていないときの要約を確認する。空欄だと畳んだ行が意味を失う。</summary>
    [Fact]
    public void ライブラリ未選択なら要約にその旨を出す()
    {
        MainViewModel viewModel = CreateViewModel(out _);

        Assert.Equal("ライブラリ未選択", viewModel.PathSummary);
    }

    /// <summary>要約が開いているライブラリに追従することを確認する。</summary>
    [Fact]
    public async Task 要約は開いているライブラリを出す()
    {
        MainViewModel viewModel = CreateViewModel(out _);
        string libraryRoot = Path.Combine(_root, "library");

        await viewModel.OpenAsync(libraryRoot);

        Assert.Equal(libraryRoot, viewModel.PathSummary);
    }

    /// <summary>作業ディレクトリを片付ける。</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // 掃除に失敗してもテストの結果は変えない。
        }
    }

    /// <summary>
    /// 何も開いていないビューモデルを作る。
    /// </summary>
    /// <param name="settingsDirectory">設定を置いたフォルダ。保存を確かめるのに使う。</param>
    /// <returns>まだ何も開いていないビューモデル。</returns>
    private MainViewModel CreateViewModel(out string settingsDirectory)
    {
        settingsDirectory = Path.Combine(_root, "settings");
        Directory.CreateDirectory(settingsDirectory);

        DictionaryStore dictionaryStore = new(settingsDirectory);
        SnapshotService snapshotService = new(() => Path.Combine(_root, "backup"));
        EmptyTagReader tagReader = new();
        NullTagWriter tagWriter = new();

        return new MainViewModel(
            new LibraryScanner(tagReader),
            snapshotService,
            new RestoreService(tagWriter, tagReader),
            new InspectionEngine(),
            dictionaryStore,
            new DictionaryViewModel(dictionaryStore),
            new ApplyService(tagWriter, tagReader, snapshotService),
            new AppSettingsStore(settingsDirectory));
    }

    /// <summary>
    /// 実ファイルを読まないタグリーダー。タグはすべて未設定として返す。
    /// </summary>
    private sealed class EmptyTagReader : ITagReader
    {
        /// <inheritdoc />
        public TrackTags Read(string fullPath, string relativePath)
        {
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

    /// <summary>
    /// 何も書かないタグライター。このテストは書き込みまで進まない。
    /// </summary>
    private sealed class NullTagWriter : ITagWriter
    {
        /// <inheritdoc />
        public void Write(string fullPath, IReadOnlyDictionary<TagField, IReadOnlyList<string>> fields)
        {
            // 書き込みは検証対象外。
        }
    }
}
