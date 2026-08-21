using MusicTagAuditor.Core.Settings;

namespace MusicTagAuditor.Core.Tests.Settings;

/// <summary>
/// 設定の読み書きのテスト。
/// **壊れた設定で起動を止めないこと**が要件なので、異常系を重点的に確認する。
/// </summary>
public sealed class AppSettingsStoreTests : IDisposable
{
    /// <summary>テスト用の設定フォルダ。</summary>
    private readonly string _directory;

    /// <summary>
    /// テスト用の一時フォルダを用意する。
    /// </summary>
    public AppSettingsStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "MusicTagAuditor.tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    /// <summary>
    /// 一時フォルダを削除する。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// 設定ファイルが無ければ既定値で始まることを確認する。
    /// </summary>
    [Fact]
    public void UsesDefaultsWhenFileIsMissing()
    {
        AppSettingsStore store = new(_directory);

        Assert.Null(store.Current.BackupRoot);
        Assert.Null(store.Current.LastLibraryRoot);
        Assert.Null(store.LoadError);
    }

    /// <summary>
    /// 保存した内容を読み戻せることを確認する。
    /// </summary>
    [Fact]
    public void RoundTripsSettings()
    {
        new AppSettingsStore(_directory).Save(
            AppSettings.Default with
            {
                BackupRoot = @"D:\バックアップ置き場",
                LastLibraryRoot = @"D:\Music Library\Classic",
            });

        AppSettingsStore reopened = new(_directory);

        Assert.Equal(@"D:\バックアップ置き場", reopened.Current.BackupRoot);
        Assert.Equal(@"D:\Music Library\Classic", reopened.Current.LastLibraryRoot);
        Assert.Null(reopened.LoadError);
    }

    /// <summary>
    /// 片方だけを書き換えても、もう片方が消えないことを確認する。
    /// バックアップ先とライブラリは別々の操作で保存されるため、
    /// 上書きし合うと直前に設定したほうが黙って失われる。
    /// </summary>
    [Fact]
    public void KeepsBackupRootWhenLastLibraryRootChanges()
    {
        AppSettingsStore store = new(_directory);
        store.Save(AppSettings.Default with { BackupRoot = @"D:\music backup" });

        store.Save(store.Current with { LastLibraryRoot = @"D:\Music Library\Classic" });

        AppSettingsStore reopened = new(_directory);

        Assert.Equal(@"D:\music backup", reopened.Current.BackupRoot);
        Assert.Equal(@"D:\Music Library\Classic", reopened.Current.LastLibraryRoot);
    }

    /// <summary>
    /// ライブラリを記憶する前に書かれた設定も読めることを確認する。
    /// 項目が増えるたびに既存の設定が捨てられるのでは、設定した意味がない。
    /// </summary>
    [Fact]
    public void ReadsSettingsWrittenBeforeLastLibraryRootExisted()
    {
        File.WriteAllText(
            AppSettingsStore.GetSettingsPath(_directory),
            """{ "backupRoot": "D:\\music backup" }""");

        AppSettingsStore store = new(_directory);

        Assert.Equal(@"D:\music backup", store.Current.BackupRoot);
        Assert.Null(store.Current.LastLibraryRoot);
        Assert.Null(store.LoadError);
    }

    /// <summary>
    /// パス欄の折り畳みを覚える前に書かれた設定も読めることを確認する。
    /// 項目が無いときは false（展開）でなければならない。既存の利用者が
    /// 更新した途端に畳んだ状態で起動するのでは、何が起きたか分からない。
    /// </summary>
    [Fact]
    public void TreatsMissingPathsCollapsedAsExpanded()
    {
        File.WriteAllText(
            AppSettingsStore.GetSettingsPath(_directory),
            """{ "backupRoot": "D:\\music backup" }""");

        AppSettingsStore store = new(_directory);

        Assert.False(store.Current.PathsCollapsed);
        Assert.Null(store.LoadError);
    }

    /// <summary>
    /// 折り畳みの状態が書き出して読み直しても残ることを確認する。
    /// </summary>
    [Fact]
    public void RoundTripsPathsCollapsed()
    {
        new AppSettingsStore(_directory).Save(AppSettings.Default with { PathsCollapsed = true });

        Assert.True(new AppSettingsStore(_directory).Current.PathsCollapsed);
    }

    /// <summary>
    /// 保存が現在の設定にも反映されることを確認する。読み直さないと効かないのでは使いにくい。
    /// </summary>
    [Fact]
    public void SaveUpdatesCurrent()
    {
        AppSettingsStore store = new(_directory);

        store.Save(AppSettings.Default with { BackupRoot = @"E:\backups" });

        Assert.Equal(@"E:\backups", store.Current.BackupRoot);
    }

    /// <summary>
    /// 既定に戻した内容も保存されることを確認する。
    /// </summary>
    [Fact]
    public void SavesNullBackupRoot()
    {
        AppSettingsStore store = new(_directory);
        store.Save(AppSettings.Default with { BackupRoot = @"E:\backups" });

        store.Save(store.Current with { BackupRoot = null });

        Assert.Null(new AppSettingsStore(_directory).Current.BackupRoot);
    }

    /// <summary>
    /// 日本語のパスがエスケープされずに書かれることを確認する。
    /// 設定ファイルは人が開いて直せることを想定している。
    /// </summary>
    [Fact]
    public void WritesJapaneseWithoutEscaping()
    {
        AppSettingsStore store = new(_directory);

        store.Save(AppSettings.Default with { BackupRoot = @"D:\音楽\バックアップ" });

        string json = File.ReadAllText(store.FilePath);

        Assert.Contains("バックアップ", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// 壊れた JSON でも例外を投げず、既定値で起動できることを確認する。
    /// 設定は入力し直せるが、起動できないと利用者には手の打ちようがない。
    /// </summary>
    [Fact]
    public void FallsBackToDefaultsWhenJsonIsBroken()
    {
        File.WriteAllText(AppSettingsStore.GetSettingsPath(_directory), "{ これは JSON ではない");

        AppSettingsStore store = new(_directory);

        Assert.Null(store.Current.BackupRoot);
        Assert.NotNull(store.LoadError);
    }

    /// <summary>
    /// 壊れた設定に上書き保存すると、以後は正常に読めることを確認する。
    /// </summary>
    [Fact]
    public void SaveClearsLoadError()
    {
        File.WriteAllText(AppSettingsStore.GetSettingsPath(_directory), "壊れている");
        AppSettingsStore store = new(_directory);

        store.Save(AppSettings.Default with { BackupRoot = @"F:\backups" });

        Assert.Null(store.LoadError);
        Assert.Equal(@"F:\backups", new AppSettingsStore(_directory).Current.BackupRoot);
    }

    /// <summary>
    /// フォルダが無くても保存できることを確認する。初回起動では %APPDATA% 配下が空。
    /// </summary>
    [Fact]
    public void CreatesDirectoryOnSave()
    {
        string nested = Path.Combine(_directory, "まだ無いフォルダ");

        new AppSettingsStore(nested).Save(AppSettings.Default with { BackupRoot = @"G:\backups" });

        Assert.True(File.Exists(AppSettingsStore.GetSettingsPath(nested)));
    }
}
