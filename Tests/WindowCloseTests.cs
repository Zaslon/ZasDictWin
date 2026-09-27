using System.Windows;
using ZasDictWin.Mediator;
using ZasDictWin.Presenters;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;
using ZasDictWin.Views;

namespace ZasDictWin.Tests;

/// <summary>
/// 利用者が × で閉じた窓（WM_CLOSE → OnClosing）は、裁定がその場で CloseFromRoot を呼び返してくる。
/// WPF は Closing の最中の Close() を例外にするので、実物の窓で通しで確かめる。
/// </summary>
public class WindowCloseTests
{
    /// <summary>App.OnStartup の WindowHostFactory と同じく、閉じる指示を窓へそのまま渡す。</summary>
    private sealed class PassThroughFactory : IHostFactory
    {
        public IUiHost OpenFloating(DockFloat host) => throw new NotSupportedException();
        public IUiHost OpenSettings(SettingsViewModel state) => throw new NotSupportedException();
        public IUiHost OpenStream() => throw new NotSupportedException();
        public IUiHost OpenCount() => throw new NotSupportedException();
        public void Close(IUiHost host) => host.CloseFromRoot();
    }

    // Application は 1 プロセスに 1 つで、そのリソースは作ったスレッドからしか引けない。
    // 窓の XAML が App.xaml のリソースを参照するので、実物の窓を使う確認は 1 つの STA スレッドにまとめる。
    [Fact]
    public void UserClosingStreamAndCountWindows_ClosesWithoutThrowing() => Sta.Run(() =>
    {
        if (Application.Current is null) new App().InitializeComponent();
        var services = new StubServices();
        var state = new MainViewModel(services.Settings, () => { });
        var root = AppRoot.Initialize(new AppMediator(services, state, state.Layout));
        root.HostFactory = new PassThroughFactory();

        foreach (var window in new Window[] { new StreamWindow(new StreamViewState()), new CountWindow(new StreamViewState()) })
        {
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.ShowActivated = false;
            window.Left = -10000;
            window.Top = -10000;
            window.Show();
            Assert.Contains((IUiHost)window, root.Hosts);

            window.Close();

            Assert.True(closed, window.GetType().Name);
            Assert.DoesNotContain((IUiHost)window, root.Hosts);
        }
    });
}
