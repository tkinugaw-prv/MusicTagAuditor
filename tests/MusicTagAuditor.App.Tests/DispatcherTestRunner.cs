using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace MusicTagAuditor.App.Tests;

/// <summary>
/// テスト本体を 1 本の STA スレッド上で走らせる係。
///
/// <c>MainViewModel</c> はファイル一覧に <see cref="System.Windows.Data.CollectionViewSource"/> の
/// 既定ビューを掛ける。**WPF の <c>CollectionView</c> は作られたスレッドからしか元コレクションを
/// 変更できない。** xUnit は <c>await</c> のたびに別のプールスレッドへ戻りうるため、
/// ビューを作ったあとにフォルダを選び直すと <c>NotSupportedException</c> で落ちる。
///
/// ディスパッチャと <see cref="DispatcherSynchronizationContext"/> を用意して継続を同じスレッドへ
/// 戻すことで、実際のアプリと同じ条件でビューモデルを動かす。
/// </summary>
internal static class DispatcherTestRunner
{
    /// <summary>スレッドの終了を待つ上限。ぶら下がったまま CI を止めない。</summary>
    private static readonly TimeSpan JOIN_TIMEOUT = TimeSpan.FromMinutes(2);

    /// <summary>
    /// テスト本体をディスパッチャスレッドで実行し、完了まで待つ。
    /// </summary>
    /// <param name="body">テスト本体。</param>
    public static void Run(Func<Task> body)
    {
        ArgumentNullException.ThrowIfNull(body);

        ExceptionDispatchInfo? failure = null;

        Thread thread = new(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

            _ = dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await body();
                }
                catch (Exception ex)
                {
                    failure = ExceptionDispatchInfo.Capture(ex);
                }
                finally
                {
                    dispatcher.InvokeShutdown();
                }
            });

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        if (!thread.Join(JOIN_TIMEOUT))
        {
            throw new TimeoutException("ディスパッチャスレッドが時間内に終了しなかった。");
        }

        // 元のスタックトレースを保ったまま呼び出し側へ投げ直す。
        failure?.Throw();
    }
}
