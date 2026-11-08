using System.Windows;
using System.Windows.Threading;

namespace ZasDictWin.Tests;

/// <summary>
/// 本物の App（App.xaml のリソース）を要るテストの実行場所。Application は 1 プロセスに 1 つで、
/// そのリソースは作ったスレッドからしか引けないので、専用の STA スレッドを 1 本だけ立てて使い回す。
/// </summary>
internal static class AppThread
{
    private static readonly Lazy<Dispatcher> Dispatcher = new(Start);

    public static void Run(Action body) => Dispatcher.Value.Invoke(body);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var app = new App();
            app.InitializeComponent();
            ZasDictWin.Views.ThemePalette.Load(app, ZasDictWin.Services.AppTheme.Dark);
            dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            ready.Set();
            System.Windows.Threading.Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }
}
