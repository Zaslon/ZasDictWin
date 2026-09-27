using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using ZasDictWin.Mediator;
using ZasDictWin.Resources;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>
/// 本体の窓。Root の子（IUiHost）として登録され、入力はすべて Intent にして根へ上げる。
/// ここに残るのは窓の枠の操作（移動・最大化・端をつまんだリサイズの比率固定）と、
/// WPF の API でしか行えない描画（ステータスを光らせる）だけ。
/// </summary>
public partial class MainWindow : Window, IUiHost
{
    private readonly MainViewModel _vm;

    /// <summary>根の裁定で閉じるところか。利用者の閉じる操作はいったん止めて裁定に回す（未保存の確認があるため）。</summary>
    private bool _closingFromRoot;

    /// <summary>自分の OnClosing から閉じたい合図を上げている最中か。</summary>
    private bool _requestingClose;

    // 縦横比固定モードの間だけ値を持つ（幅 / 高さ）。null なら WndProc は何もしない。
    // 比率は設定を適用した瞬間の幅・高さから決まり、以後は端をつまんだリサイズがこれを崩さない。
    private double? _aspectRatio;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        PreviewKeyDown += OnPreviewKeyDown;
        KeyDown += OnShortcutKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
        StateChanged += (_, _) =>
        {
            UpdateMaximizeRestoreIcon();
            this.RaiseIntent(IntentKind.WindowStateChanged, WindowState);
        };
        // 独立ウィンドウは本体を Owner に持つので、本体に HWND ができてから開かせる。
        Loaded += (_, _) => this.RaiseIntent(IntentKind.WindowActivated);
        UpdateMaximizeRestoreIcon();

        // 一覧（DropDown / MenuButton）は窓の最上段の AdornerDecorator に描かれ、中身の木（Root）の外にある。
        // そこから上がる Intent も拾えるよう、受け口は窓そのものにする。
        AppRoot.Current.Attach(this, this);
        Closed += (_, _) => AppRoot.Current.Detach(this);
    }

    // ---- IUiHost ----------------------------------------------------------------

    public Guid HostId { get; } = Guid.NewGuid();

    public HostRole Role => HostRole.Shell;

    public DockNode? DockRoot => _vm.Layout.Root;

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

    // ---- 根から受ける描画の指示 ------------------------------------------------------

    /// <summary>窓の大きさと比率固定。幅・高さが NaN なら大きさは変えない（最大化中に上書きすると
    /// 元に戻したときの大きさが狂うため、裁定側が NaN で渡してくる）。</summary>
    public void ApplyShellSize(double width, double height, double? aspectRatio, bool maximize)
    {
        if (!double.IsNaN(width)) Width = width;
        if (!double.IsNaN(height)) Height = height;
        _aspectRatio = aspectRatio;
        if (maximize) WindowState = WindowState.Maximized;
    }

    // Status は差分の無い書き換えでは光らせない（裁定側が変わったときだけ指示する）。
    // 連続で変わっても Storyboard.Begin は前回分を上書きするだけで済む。
    public void FlashStatus() => ((Storyboard)Resources["StatusFlashStoryboard"]).Begin(StatusFlashBorder);

    // WindowChrome 導入前から ResizeBorderThickness だけ残して端をつまむリサイズを効かせている
    // （MainWindow.xaml 参照）ため、比率固定も WM_SIZING を横取りする形でしか実現できない
    // （WPF の Width/Height バインディングでは、ドラッグ中のリアルタイムな矯正に間に合わない）。
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(WndProc);
    }

    private const int WM_SIZING = 0x0214;
    // WM_SIZING の wParam（どの辺・角をつまんでいるか）。
    private const int WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOP = 3, WMSZ_TOPLEFT = 4,
        WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6, WMSZ_BOTTOMLEFT = 7, WMSZ_BOTTOMRIGHT = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>
    /// 縦横比固定モードのときだけ、つまんだ辺・角に応じて動いた側の一辺を比率どおりに矯正する。
    /// RECT はスクリーン座標（物理ピクセル）だが、比率は幅と高さの比なので DPI 換算は要らない。
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_SIZING || _aspectRatio is not { } ratio) return IntPtr.Zero;

        var rect = Marshal.PtrToStructure<RECT>(lParam);
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        switch (wParam.ToInt32())
        {
            // 左右の辺：幅にあわせて高さを決め、下端を動かす。
            case WMSZ_LEFT or WMSZ_RIGHT:
                rect.Bottom = rect.Top + (int)Math.Round(width / ratio);
                break;
            // 上下の辺：高さにあわせて幅を決め、右端を動かす。
            case WMSZ_TOP or WMSZ_BOTTOM:
                rect.Right = rect.Left + (int)Math.Round(height * ratio);
                break;
            // 角：左端が動く角は幅を高さから、それ以外は高さを幅から決める。
            case WMSZ_TOPLEFT:
                rect.Left = rect.Right - (int)Math.Round(height * ratio);
                break;
            case WMSZ_BOTTOMLEFT:
                rect.Bottom = rect.Top + (int)Math.Round(width / ratio);
                break;
            case WMSZ_TOPRIGHT:
                rect.Top = rect.Bottom - (int)Math.Round(width / ratio);
                break;
            case WMSZ_BOTTOMRIGHT:
                rect.Bottom = rect.Top + (int)Math.Round(width / ratio);
                break;
        }

        Marshal.StructureToPtr(rect, lParam, true);
        handled = true;
        return IntPtr.Zero;
    }

    // ---- 入力 → Intent -----------------------------------------------------------

    // Esc の行き先（一覧 → ドラッグ → 確認ダイアログ → 最後に触ったタブ）は根が決める。
    // ここは Preview（＝ウィンドウが最初に見る段）なので、Esc が子の画面に食われる前に拾える。
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        this.RaiseIntent(IntentKind.CancelRequested);
        e.Handled = true;
    }

    // ショートカットは子の画面が使わなかったキーだけを拾う（Preview ではなく KeyDown）。
    // 単語エディタの Ctrl+Enter（保存）や、検索欄・一覧の素の Enter が先に処理される。
    private void OnShortcutKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        IntentKind? kind = Keyboard.Modifiers switch
        {
            ModifierKeys.Control => key switch
            {
                Key.O => IntentKind.OpenDictionaryRequested,
                Key.S => IntentKind.SaveRequested,
                // どちらも検索欄の文字列を見出し語の初期値にして単語エディタを開く
                Key.Enter or Key.N => IntentKind.NewWordRequested,
                Key.E => IntentKind.EditWordRequested,
                Key.D => IntentKind.DuplicateWordRequested,
                _ => null,
            },
            ModifierKeys.Control | ModifierKeys.Shift when key == Key.S => IntentKind.SaveAsRequested,
            _ => null,
        };
        if (kind is not { } intent) return;
        this.RaiseIntent(intent);
        e.Handled = true;
    }

    // Ctrl＋ホイールで文字サイズを増減する。Preview（＝ウィンドウが最初に見る段）で拾って畳むので、
    // ホイールを食う一覧・本文（ScrollViewer や選択できる本文）の上でも同じように効く。
    // ブラウザのタブ（WebView2）は別 HWND なのでここには届かず、WebView2 自身の拡大縮小が働く。
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || e.Delta == 0) return;
        // 目盛りの大きさ（Delta）は機種差があるので、向きだけを見て 1 段ずつ動かす。
        this.RaiseIntent(IntentKind.ZoomFontRequested, Math.Sign(e.Delta));
        e.Handled = true;
    }

    // 標準の枠が無いので、ヘッダの余白（ボタンの無い部分）をドラッグでの移動とダブルクリックでの
    // 最大化トグルに使う。ボタンは ButtonBase が MouseLeftButtonDown を Handled 済みにするので、
    // ここまでは届かず衝突しない。
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }
        // 最大化中に掴んだ場合、WPF が自動で解除してカーソル位置に応じた大きさへ戻す。
        DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) => ToggleMaximizeRestore();

    private void Close_Click(object sender, RoutedEventArgs e) => RequestClose();

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
            // 閉じてよいか（未保存の確認）と、閉じる前の保存は根が決める。閉じるときは CloseFromRoot で戻ってくる。
            e.Cancel = true;
            _requestingClose = true;
            try { RequestClose(); }
            finally { _requestingClose = false; }
            if (!_closingFromRoot) return;
            e.Cancel = false;
        }
        base.OnClosing(e);
    }

    /// <summary>閉じたいという合図を上げる。窓の大きさは最大化・最小化中の値ではなく
    /// 通常時の矩形（RestoreBounds）を渡す（最小化したまま閉じた場合も、その前の通常時の矩形が残っている）。</summary>
    private void RequestClose()
    {
        var bounds = WindowState == WindowState.Normal
            ? new WindowBounds(Width, Height, false)
            : new WindowBounds(RestoreBounds.Width, RestoreBounds.Height, WindowState == WindowState.Maximized);
        this.RaiseIntent(IntentKind.WindowCloseRequested, bounds);
    }
}
