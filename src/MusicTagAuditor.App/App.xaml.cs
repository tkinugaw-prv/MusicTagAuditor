using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MusicTagAuditor.App.ViewModels;
using MusicTagAuditor.Core.Abstractions;
using MusicTagAuditor.Core.Applying;
using MusicTagAuditor.Core.Backup;
using MusicTagAuditor.Core.Dictionary;
using MusicTagAuditor.Core.Inspection;
using MusicTagAuditor.Core.Scanning;
using MusicTagAuditor.TagIo;
using Serilog;

namespace MusicTagAuditor.App;

/// <summary>
/// アプリケーションのエントリポイント。DI コンテナとログの初期化を行う。
/// </summary>
public partial class App : Application
{
    /// <summary>サービスプロバイダ。</summary>
    private ServiceProvider? _services;

    /// <summary>
    /// 起動時にログと DI を構成し、メインウィンドウを表示する。
    /// </summary>
    /// <param name="e">起動イベントの引数。</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ConfigureLogging();

        ServiceCollection services = new();
        services.AddSingleton<ITagReader, TagReader>();
        services.AddSingleton<ITagWriter, TagWriter>();
        services.AddSingleton<ScanOptions>();
        services.AddSingleton<LibraryScanner>();
        services.AddSingleton<SnapshotService>();
        services.AddSingleton<RestoreService>();
        services.AddSingleton<ApplyService>();
        // ルールを明示的に登録する。登録しないと DI は空のコレクションを注入し、
        // 検査が常に 0 件になる（例外も出ないため気づきにくい）。
        foreach (IInspectionRule rule in InspectionEngine.CreateDefaultRules())
        {
            services.AddSingleton(rule);
        }

        services.AddSingleton<InspectionEngine>();

        // 辞書は %APPDATA%\MusicTagAuditor に置く。初回は同梱の既定辞書がコピーされる。
        // 索引ではなくストアを登録する。段階 5 で辞書を編集できるようになり、
        // 索引は保存のたびに作り直されるため、握り込むと古いものを参照し続けてしまう。
        services.AddSingleton(_ => new DictionaryStore(AppConst.GetAppDataDirectory()));
        services.AddSingleton<DictionaryViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _services = services.BuildServiceProvider();

        InspectionEngine engine = _services.GetRequiredService<InspectionEngine>();

        Log.Information("Music Tag Auditor を起動した 検査ルール={RuleCount} 件", engine.RuleCount);

        _services.GetRequiredService<MainWindow>().Show();

        // 第 1 引数にライブラリのパスが渡されていればそのまま開く。
        if (e.Args.Length > 0 && Directory.Exists(e.Args[0]))
        {
            MainViewModel viewModel = _services.GetRequiredService<MainViewModel>();
            _ = Dispatcher.InvokeAsync(async () => await viewModel.OpenAsync(e.Args[0]));
        }
    }

    /// <summary>
    /// 終了時にログを確実に書き出す。
    /// </summary>
    /// <param name="e">終了イベントの引数。</param>
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Music Tag Auditor を終了した");
        Log.CloseAndFlush();

        _services?.Dispose();

        base.OnExit(e);
    }

    /// <summary>
    /// Serilog をファイル出力で構成する。適用処理の事後追跡にログが必要（docs/SPEC.md 11章）。
    /// </summary>
    private static void ConfigureLogging()
    {
        string logDirectory = AppConst.GetLogDirectory();
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDirectory, AppConst.LOG_FILE_NAME),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: AppConst.LOG_RETAINED_FILE_COUNT,
                encoding: System.Text.Encoding.UTF8)
            .CreateLogger();
    }
}
