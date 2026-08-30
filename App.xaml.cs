using System.Windows;
using System.Windows.Threading;
using ZasDictWin.Services;

namespace ZasDictWin;

public partial class App : Application
{
    // 短時間にこれ以上つづけざま例外を拾ったら「続けても同じことを繰り返す」状態とみなし、
    // 画面が固まったまま動かないより早く終わらせます。
    private const int MaxContinuations = 5;
    private static readonly TimeSpan GuardSpan = TimeSpan.FromSeconds(10);
    private static readonly List<DateTime> Recent = new();

    /// <summary>UI スレッドで握りつぶした例外。MainWindow がオーバーレイ表示のために購読します。</summary>
    public static event Action<Exception>? UiException;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 既定の MessageBox は別 HWND なので OBS に映りません。ここでは記録と継続判断だけを行い、
        // 目に見える案内は MainWindow 側のオーバーレイに任せます。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ErrorLog.Write("AppDomain", args.ExceptionObject as Exception
                ?? new Exception(args.ExceptionObject?.ToString() ?? "不明な障害"));

        base.OnStartup(e);   // StartupUri の MainWindow はここで生成されるので登録は先に済ませる
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write("Dispatcher", e.Exception);
        if (!AllowContinuation()) return;

        e.Handled = true;
        UiException?.Invoke(e.Exception);
    }

    private static bool AllowContinuation()
    {
        var now = DateTime.UtcNow;
        Recent.RemoveAll(t => now - t > GuardSpan);
        if (Recent.Count >= MaxContinuations) return false;

        Recent.Add(now);
        return true;
    }
}

