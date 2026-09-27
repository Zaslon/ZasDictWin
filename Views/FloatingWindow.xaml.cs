using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ZasDictWin.Resources;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>
/// 本体の窓から持ち出したタブを入れる独立ウィンドウ。中身は本体と同じ割り付けなので、
/// この窓のタブも掴んで本体へ運び戻せる（<see cref="OverlayDrag"/>）。
/// 窓の開け閉めは根の裁定（HostCommand）で行い、ここは位置・大きさの適用と、本体と同じ入力を Intent にして上げるだけ。
/// </summary>
public partial class FloatingWindow : Window, IUiHost
{
    /// <summary>窓の端がこれだけ画面に残るように置き直す。画面構成が変わっても掴めなくならないように。</summary>
    private const double MinVisible = 80;

    private readonly DockFloat _host;

    /// <summary>根の裁定で閉じるところか。利用者の閉じる操作はいったん止めて裁定に回す（中のタブを始末するため）。</summary>
    private bool _closingFromRoot;

    /// <summary>自分の OnClosing から閉じたい合図を上げている最中か。</summary>
    private bool _requestingClose;

    public FloatingWindow(DockFloat host)
    {
        InitializeComponent();
        _host = host;
        DataContext = host;
        ApplyBounds();

        LocationChanged += (_, _) => RaiseBounds();
        SizeChanged += (_, _) => RaiseBounds();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
        StateChanged += (_, _) => UpdateMaximizeRestoreIcon();
        UpdateMaximizeRestoreIcon();

        // 一覧（DropDown / MenuButton）は窓の最上段の AdornerDecorator に描かれるので、受け口は窓そのものにする。
        AppRoot.Current.Attach(this, this);
        Closed += (_, _) => AppRoot.Current.Detach(this);
    }

    // ---- IUiHost ----------------------------------------------------------------

    public Guid HostId => _host.Id;

    public HostRole Role => HostRole.Floating;

    public DockNode? DockRoot => _host.Root;

    public Rect BoundsDip => new(Left, Top, Width, Height);

    public bool IsActiveHost => IsActive;

    public void CloseFromRoot()
    {
        _closingFromRoot = true;
        // 合図の裁定が同期で閉じると決めた場合は、進行中の Close をそのまま通す
        // （Closing の最中に Close() を呼ぶと WPF が InvalidOperationException を投げる）。
        if (!_requestingClose) Close();
    }

    public void FocusFromRoot() => Activate();

    public bool TryHitLeaf(Point screen, out int leafId, out Size leafSize, out Point leafLocal)
        => WindowHitTest.TryHitLeaf(this, screen, out leafId, out leafSize, out leafLocal);

    public bool ContainsScreenPoint(Point screen) => WindowHitTest.Contains(this, screen);

    // ---- 描画パラメータの適用 -------------------------------------------------------

    private void ApplyBounds()
    {
        var bounds = _host.Bounds;
        Width = Math.Max(bounds.Width, MinWidth);
        Height = Math.Max(bounds.Height, MinHeight);

        // 位置を決めていない窓（メニューから開いたツールなど）は本体の中央に出す。
        if (double.IsNaN(bounds.X) || double.IsNaN(bounds.Y))
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = Math.Clamp(bounds.X,
            SystemParameters.VirtualScreenLeft - Width + MinVisible,
            SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - MinVisible);
        Top = Math.Clamp(bounds.Y,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - MinVisible);
    }

    // ---- 入力 → Intent -----------------------------------------------------------

    // 最大化・最小化中の値は画面いっぱい（あるいは無意味な位置）なので、通常時だけ知らせる。
    private void RaiseBounds()
    {
        if (WindowState != WindowState.Normal) return;
        this.RaiseIntent(IntentKind.WindowBoundsChanged, new Rect(Left, Top, Width, Height));
    }

    // Esc の行き先は根が決める（この窓ではその窓に出ているタブを閉じる）。
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        this.RaiseIntent(IntentKind.CancelRequested);
        e.Handled = true;
    }

    // 文字サイズ倍率はアプリ全体で 1 つなので、どの窓で回しても同じ値を動かす。
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || e.Delta == 0) return;
        this.RaiseIntent(IntentKind.ZoomFontRequested, Math.Sign(e.Delta));
        e.Handled = true;
    }

    // 標準の枠が無いので、ヘッダの余白をドラッグでの移動とダブルクリックでの最大化トグルに使う
    // （MainWindow.Header_MouseLeftButtonDown と同じ理由）。
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }
        DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) => ToggleMaximizeRestore();

    private void Close_Click(object sender, RoutedEventArgs e) => this.RaiseIntent(IntentKind.WindowCloseRequested);

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void UpdateMaximizeRestoreIcon()
    {
        var maximized = WindowState == WindowState.Maximized;
        // MDL2 Assets: ChromeMaximize (E922) / ChromeRestore (E923)
        MaximizeRestoreButton.Content = maximized ? "" : "";
        MaximizeRestoreButton.ToolTip = maximized ? Strings.Common_Restore : Strings.Common_Maximize;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closingFromRoot)
        {
            e.Cancel = true;
            _requestingClose = true;
            try { this.RaiseIntent(IntentKind.WindowCloseRequested); }
            finally { _requestingClose = false; }
            if (!_closingFromRoot) return;
            e.Cancel = false;
        }
        base.OnClosing(e);
    }
}
