using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Web.WebView2.Core;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>
/// ブラウザのタブ。WebView2 は airspace のため WPF 描画より手前に出るが、
/// メインウィンドウの子 HWND なので OBS のウィンドウキャプチャにはそのまま映る。
/// </summary>
public partial class BrowserPanel : UserControl
{
    private const string RuntimeMissingMessage =
        "WebView2 を起動できませんでした。Microsoft Edge または WebView2 Runtime をインストールすると使えます。";

    private BrowserViewModel? _vm;
    private Task? _init;
    private string? _pendingUrl;

    public BrowserPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) =>
        {
            if (_vm?.IsOpen == true) EnsureReady(null);
            DropDown.OverlayVisibilityChanged += OnOverlayVisibilityChanged;
        };
        Unloaded += (_, _) => DropDown.OverlayVisibilityChanged -= OnOverlayVisibilityChanged;
    }

    /// <summary>プルダウンや階層メニューを開いている間は WebView2 を隠す。airspace のせいで
    /// Visibility を手前に重ねる通常の Z 順制御が効かず、隠す以外に取れる手が無いため。
    /// Web.Visibility は HasError にバインドしてあるので、SetValue で直接上書きするとバインドが
    /// 外れてしまう。SetCurrentValue で一時的に上書きし、閉じたらバインドから再評価させて戻す。</summary>
    private void OnOverlayVisibilityChanged(bool overlayOpen)
    {
        if (overlayOpen)
        {
            Web.SetCurrentValue(VisibilityProperty, Visibility.Hidden);
        }
        else
        {
            BindingOperations.GetBindingExpressionBase(Web, VisibilityProperty)?.UpdateTarget();
        }
    }

    private void Attach()
    {
        if (_vm is not null)
        {
            _vm.InitializeRequested -= OnInitialize;
            _vm.NavigateRequested -= OnNavigate;
            _vm.BackRequested -= OnBack;
            _vm.ForwardRequested -= OnForward;
            _vm.ReloadRequested -= OnReload;
        }

        _vm = DataContext as BrowserViewModel;
        if (_vm is null) return;

        _vm.InitializeRequested += OnInitialize;
        _vm.NavigateRequested += OnNavigate;
        _vm.BackRequested += OnBack;
        _vm.ForwardRequested += OnForward;
        _vm.ReloadRequested += OnReload;
    }

    private void OnInitialize()
    {
        // 開き直しで Core が生きている場合はページ状態をそのまま維持する。
        if (Web.CoreWebView2 is not null) return;
        EnsureReady(null);
    }

    private void OnNavigate(string url)
    {
        if (Web.CoreWebView2 is not null) NavigateTo(url);
        else EnsureReady(url);   // 初期化が済んでいなければ完了後にそこへ飛ぶ
    }

    private void OnBack() { if (Web.CoreWebView2?.CanGoBack == true) Web.CoreWebView2.GoBack(); }

    private void OnForward() { if (Web.CoreWebView2?.CanGoForward == true) Web.CoreWebView2.GoForward(); }

    private void OnReload() => Web.CoreWebView2?.Reload();

    /// <summary>WebView2 の初期化を開始する。初期化そのものは一度だけ行う。</summary>
    private void EnsureReady(string? url)
    {
        if (url is not null) _pendingUrl = url;
        _init ??= InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (_vm is null) return;
        try
        {
            // 既定だと exe の隣にユーザーデータを作るため、Program Files 配置では失敗する。設定置き場に寄せる。
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZasDictWin", "WebView2");
            Directory.CreateDirectory(dir);

            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dir);
            await Web.EnsureCoreWebView2Async(env);

            var core = Web.CoreWebView2;
            if (core is null)
            {
                _vm.ReportError(RuntimeMissingMessage);
                return;
            }

            // 配信中に余計なメニューや DevTools が出ないよう、既定の UI は止めておく。
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;

            core.SourceChanged += (_, _) =>
            {
                _vm.ReportAddress(core.Source?.ToString());
                ReportHistory(core);
            };
            core.DocumentTitleChanged += (_, _) => _vm.ReportTitle(core.DocumentTitle);
            core.NavigationStarting += (_, e) =>
            {
                _vm.ReportError(null);
                _vm.ReportBusy(true);
                _vm.ReportStatus($"読み込み中: {e.Uri}");
            };
            core.NavigationCompleted += (_, e) =>
            {
                _vm.ReportBusy(false);
                ReportHistory(core);
                var title = core.DocumentTitle;
                _vm.ReportStatus(e.IsSuccess
                    ? (string.IsNullOrEmpty(title) ? core.Source?.ToString() ?? "" : title)
                    : $"読み込みに失敗しました（{e.WebErrorStatus}）");
            };
            // target=_blank のリンクは同じタブで開く（別ウィンドウは OBS に映らない）。
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                core.Navigate(e.Uri);
            };

            var first = _pendingUrl ?? (string.IsNullOrEmpty(_vm.Address) ? _vm.StartUrl : _vm.Address);
            _pendingUrl = null;
            NavigateTo(first);
        }
        catch (Exception ex)
        {
            // ランタイム未導入・プロセス生成失敗・ポリシー制限など例外の種類が多いため絞り込まない。
            _init = null;   // 次回開いたときに再試行できるようにする
            _vm.ReportError($"{RuntimeMissingMessage}{Environment.NewLine}{ex.Message}");
        }
    }

    private void NavigateTo(string url)
    {
        if (_vm is null) return;
        try
        {
            Web.CoreWebView2?.Navigate(url);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or COMException)
        {
            _vm.ReportStatus($"開けません: {ex.Message}");
        }
    }

    private void ReportHistory(CoreWebView2 core) => _vm?.ReportHistoryState(core.CanGoBack, core.CanGoForward);
}
