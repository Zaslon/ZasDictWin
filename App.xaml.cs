using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using ZasDictWin.Mediator;
using ZasDictWin.Presenters;
using ZasDictWin.Resources;
using ZasDictWin.Root;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;
using ZasDictWin.Views;

namespace ZasDictWin;

public partial class App : Application
{
    // 短時間にこれ以上つづけざま例外を拾ったら「続けても同じことを繰り返す」状態とみなし、
    // 画面が固まったまま動かないより早く終わらせます。
    private const int MaxContinuations = 5;
    private static readonly TimeSpan GuardSpan = TimeSpan.FromSeconds(10);
    private static readonly List<DateTime> Recent = new();

    private AppRoot? _root;

    protected override void OnStartup(StartupEventArgs e)
    {
        // XAML の x:Static は各ウィンドウの InitializeComponent で一度だけ評価されるので、
        // UI 文字列の言語は窓を作るより前に確定させておく必要がある。
        var settings = AppSettings.Load();
        ApplyLanguage(settings.Language);

        // 既定の MessageBox は別 HWND なので OBS に映りません。ここでは記録と継続判断だけを行い、
        // 目に見える案内は確認ダイアログの層（根の裁定）に任せます。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ErrorLog.Write("AppDomain", args.ExceptionObject as Exception
                ?? new Exception(args.ExceptionObject?.ToString() ?? "不明な障害"));

        base.OnStartup(e);

        var state = new MainViewModel(settings);
        var mediator = new AppMediator(new AppServices(settings), state, state.Layout)
        {
            DragThreshold = new Size(SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance),
        };
        _root = AppRoot.Initialize(mediator);
        _ = new DockPresenter(mediator, state.Layout);
        _ = new ShellPresenter(mediator, state, _root);
        var stream = new StreamPresenter(mediator, state, _root);
        // 窓の工場は Mediator が最初の裁定（起動時の復元）を出す前に差し込む。
        _root.HostFactory = new WindowHostFactory(_root, stream.View);

        var window = new MainWindow(state);
        MainWindow = window;
        mediator.Start();
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write("Dispatcher", e.Exception);
        if (!AllowContinuation()) return;

        e.Handled = true;
        _root?.Mediator.Dispatch(new Intent(IntentKind.UnhandledExceptionRaised, e.Exception, new IntentContext(), null));
    }

    private static bool AllowContinuation()
    {
        var now = DateTime.UtcNow;
        Recent.RemoveAll(t => now - t > GuardSpan);
        if (Recent.Count >= MaxContinuations) return false;

        Recent.Add(now);
        return true;
    }

    /// <summary>UI 文字列の言語を確定させる。CurrentCulture（日付・数値の書式）には触れない。
    /// Strings.Get の参照先切り替えは Strings.SetLanguage が担う（Idyerin のような ICU に無い
    /// 自作言語コードでは CultureInfo ベースの解決ができないため。Strings.cs の説明を参照）。
    /// CurrentUICulture / DefaultThreadCurrentUICulture 自体は EllipsisMiddle の文字整形など
    /// 文字列引き当て以外でも参照されるので、こちらは従来どおり合わせておく
    /// （未知のコードでも CultureInfo の生成自体は例外にならないので安全）。
    /// 対応言語の一覧は Languages.All（Resources/Languages.cs）を参照。</summary>
    private static void ApplyLanguage(string language)
    {
        var code = Languages.Normalize(language);
        Strings.SetLanguage(code);

        var culture = new CultureInfo(code);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    /// <summary>
    /// 根の裁定（HostCommand）を受けて窓を開け閉めする View 層の実行役。
    /// 窓ごとの Owner の有無はここで決める（OBS のキャプチャと当たり判定の前提）。
    /// </summary>
    private sealed class WindowHostFactory : IHostFactory
    {
        private readonly AppRoot _root;
        private readonly StreamViewState _stream;
        private DragGhost? _ghost;

        public WindowHostFactory(AppRoot root, StreamViewState stream)
        {
            _root = root;
            _stream = stream;
            root.ViewCommandIssued += OnViewCommand;
        }

        private MainWindow? Shell => _root.Shell as MainWindow;

        /// <summary>独立ウィンドウは Owner を本体にする。必ず本体より手前に出るので、タブの運び先の
        /// 当たり判定はこの前後関係を前提にしている（本体に HWND ができてからでないと開けない）。</summary>
        public IUiHost OpenFloating(DockFloat host)
        {
            var window = new FloatingWindow(host) { Owner = Shell };
            window.Show();
            return window;
        }

        /// <summary>設定は Owner を本体にしたモーダル。ShowDialog は入れ子のメッセージループで戻ってこないので、
        /// 裁定の途中で止まらないよう、開くのは今の裁定が終わってからにする。</summary>
        public IUiHost OpenSettings(SettingsViewModel state)
        {
            var window = new SettingsWindow(state) { Owner = Shell };
            window.Dispatcher.BeginInvoke(() => window.ShowDialog());
            return window;
        }

        /// <summary>単語ウィンドウ・単語数ウィンドウは Owner を持たない。OBS で個別のウィンドウ
        /// キャプチャソースとして選べる独立した HWND にするため。</summary>
        public IUiHost OpenStream()
        {
            var window = new StreamWindow(_stream);
            window.Show();
            return window;
        }

        public IUiHost OpenCount()
        {
            var window = new CountWindow(_stream);
            window.Show();
            return window;
        }

        public void Close(IUiHost host) => host.CloseFromRoot();

        private void OnViewCommand(HostCommand command)
        {
            switch (command)
            {
                case HostCommand.ApplyShellSize size:
                    Shell?.ApplyShellSize(size.Width, size.Height, size.AspectRatio, size.Maximize);
                    break;
                case HostCommand.ShowDragGhost ghost:
                    if (_ghost is null)
                    {
                        _ghost = new DragGhost(ghost.Title);
                        _ghost.MoveTo(ghost.AtDip);
                        _ghost.Show();
                    }
                    else _ghost.MoveTo(ghost.AtDip);
                    break;
                case HostCommand.HideDragGhost:
                    var shown = _ghost;
                    _ghost = null;
                    shown?.Close();
                    break;
                case HostCommand.FlashStatus:
                    Shell?.FlashStatus();
                    break;
            }
        }
    }
}
