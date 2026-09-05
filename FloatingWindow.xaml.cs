using System.Windows;
using System.Windows.Input;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>
/// 本体の窓から持ち出したタブを入れる独立ウィンドウ。中身は本体と同じ割り付けなので、
/// この窓のタブも掴んで本体へ運び戻せる（<see cref="OverlayDrag"/>）。
/// 窓の開け閉めそのものは <see cref="MainWindow"/> が割り付けの通知を見て行い、
/// ここは位置・大きさの書き戻しと、本体と同じキー操作を受け持つ。
/// </summary>
public partial class FloatingWindow : Window
{
    /// <summary>窓の端がこれだけ画面に残るように置き直す。画面構成が変わっても掴めなくならないように。</summary>
    private const double MinVisible = 80;

    private readonly MainViewModel _main;
    private readonly DockFloat _host;

    /// <summary>割り付け側から閉じたか。手で閉じたときだけ中のタブを始末する必要がある。</summary>
    private bool _fromLayout;

    public FloatingWindow(MainViewModel main, DockFloat host)
    {
        InitializeComponent();
        _main = main;
        _host = host;
        DataContext = host;
        ApplyBounds();

        LocationChanged += (_, _) => Remember();
        SizeChanged += (_, _) => Remember();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
        StateChanged += (_, _) => UpdateMaximizeRestoreIcon();
        UpdateMaximizeRestoreIcon();
    }

    /// <summary>割り付け側の都合で閉じる（中身が本体へ移って空になった窓）。</summary>
    public void CloseFromLayout()
    {
        _fromLayout = true;
        Close();
    }

    /// <summary>手で閉じたか。閉じた後に中のタブを始末すべきかの判断に使う。</summary>
    public bool ClosedByUser => !_fromLayout;

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

    // 最大化・最小化中の値は画面いっぱい（あるいは無意味な位置）なので、通常時だけ覚える。
    private void Remember()
    {
        if (WindowState != WindowState.Normal) return;
        _host.Bounds = new Rect(Left, Top, Width, Height);
    }

    // 本体の Esc と同じ順で畳む。確認ダイアログは本体の窓にしか無いので、ここでは扱わない。
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (DropDown.CloseCurrent()) { e.Handled = true; return; }
        if (MenuButton.CloseCurrent()) { e.Handled = true; return; }
        if (AreaDrag.Cancel()) { e.Handled = true; return; }
        if (OverlayDrag.Cancel()) { e.Handled = true; return; }
        // この窓に出ているタブだけを閉じる（据え置きのタブは閉じられないので飛ばす）。
        if (_host.Leaves.FirstOrDefault(l => l.Selected is { IsPinned: false })?.Selected is not { } vm) return;
        vm.CloseCommand.Execute(null);
        e.Handled = true;
    }

    // 文字サイズ倍率はアプリ全体で 1 つなので、どの窓で回しても同じ値を動かす。
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || e.Delta == 0) return;
        _main.ZoomFont(Math.Sign(e.Delta));
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

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void UpdateMaximizeRestoreIcon()
    {
        var maximized = WindowState == WindowState.Maximized;
        // MDL2 Assets: ChromeMaximize (E922) / ChromeRestore (E923)
        MaximizeRestoreButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeRestoreButton.ToolTip = maximized ? "元のサイズに戻す" : "最大化";
    }
}
