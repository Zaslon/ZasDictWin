using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ZasDictWin.Mediator;
using ZasDictWin.Resources;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>
/// 本体の窓。Root の子（IUiHost）として登録され、入力はすべて Intent にして根へ上げる。
/// ここに残るのは窓の枠の操作（移動・最大化）と、
/// WPF の API でしか行えない描画（ステータスを光らせる）だけ。
/// </summary>
public partial class MainWindow : Window, IUiHost
{
    private readonly MainViewModel _vm;

    /// <summary>根の裁定で閉じるところか。利用者の閉じる操作はいったん止めて裁定に回す（未保存の確認があるため）。</summary>
    private bool _closingFromRoot;

    /// <summary>自分の OnClosing から閉じたい合図を上げている最中か。</summary>
    private bool _requestingClose;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        PreviewMouseDown += (_, _) => _altAlone = false;
        KeyDown += OnShortcutKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
        StateChanged += (_, _) =>
        {
            UpdateMaximizeRestoreIcon();
            this.RaiseIntent(IntentKind.WindowStateChanged, WindowState);
            RaiseBounds();
        };
        // メニューバーの［終了］は × と違って窓の大きさを渡さないので、大きさは変わるたびに控えさせておく。
        SizeChanged += (_, _) => RaiseBounds();
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

    public bool TryHitLeaf(Point screen, out int leafId, out Size leafSize, out Point leafLocal, out double tabStripHeight, out int tabSlot)
        => WindowHitTest.TryHitLeaf(this, screen, out leafId, out leafSize, out leafLocal, out tabStripHeight, out tabSlot);

    public bool ContainsScreenPoint(Point screen) => WindowHitTest.Contains(this, screen);

    // ---- 根から受ける描画の指示 ------------------------------------------------------

    /// <summary>窓の大きさ。幅・高さが NaN なら大きさは変えない（最大化中に上書きすると
    /// 元に戻したときの大きさが狂うため、裁定側が NaN で渡してくる）。</summary>
    public void ApplyShellSize(double width, double height, bool maximize)
    {
        if (!double.IsNaN(width)) Width = width;
        if (!double.IsNaN(height)) Height = height;
        if (maximize) WindowState = WindowState.Maximized;
    }

    // 裁定側は警告・失敗のときだけ指示する。連続で来ても Storyboard.Begin は前回分を上書きするだけで済む。
    // OS の視覚効果（アニメーション）を切っている場合は、強調の色を一定時間だけ出してアニメーション無しで戻す
    // （ZasDesignSystem implementation/common.md「ステータスバー」）。
    public void FlashStatus(StatusSemantic semantic)
    {
        StatusFlashBrush.Color = (Color)FindResource(semantic switch
        {
            StatusSemantic.Error => "StatusErrorColor",
            StatusSemantic.Warning => "StatusWarningColor",
            StatusSemantic.Success => "StatusSuccessColor",
            _ => "StatusInfoColor",
        });
        var key = SystemParameters.ClientAreaAnimation ? "StatusFlashStoryboard" : "StatusFailureHoldStoryboard";
        ((Storyboard)Resources[key]).Begin(StatusFlashBorder);
    }

    // ---- 入力 → Intent -----------------------------------------------------------

    private void RaiseBounds()
    {
        if (!IsLoaded) return;
        var bounds = WindowState == WindowState.Normal
            ? new WindowBounds(Width, Height, false)
            : new WindowBounds(RestoreBounds.Width, RestoreBounds.Height, WindowState == WindowState.Maximized);
        this.RaiseIntent(IntentKind.WindowBoundsChanged, bounds);
    }

    // ---- メニューバー ---------------------------------------------------------------

    /// <summary>Alt を単独で押して離したか。間に他のキーやクリックが入ったら、Alt＋文字などの組み合わせとみなす。</summary>
    private bool _altAlone;

    private bool MenuBarFocused => MenuBar.IsKeyboardFocusWithin;

    private IEnumerable<MenuButton> MenuBarItems => MenuBar.Children.OfType<MenuButton>().Where(m => m.IsVisible && m.IsEnabled);

    /// <summary>Alt か F10 でメニューバーへ移る。メニューバーにいるときは、移る前の要素へ戻る
    /// （ZasDesignSystem rules.md「パソコンのソフトにはメニューバーを設ける」）。</summary>
    private void ToggleMenuBar()
    {
        if (MenuBarFocused) MenuButton.LeaveMenuBar();
        else MenuBarItems.FirstOrDefault()?.Focus();
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.System && e.SystemKey is Key.LeftAlt or Key.RightAlt && _altAlone)
        {
            _altAlone = false;
            ToggleMenuBar();
            e.Handled = true;
        }
    }

    // Esc の行き先（一覧 → ドラッグ → 確認ダイアログ → 最後に触ったタブ）は根が決める。
    // ここは Preview（＝ウィンドウが最初に見る段）なので、Esc が子の画面に食われる前に拾える。
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (e.Key == Key.System && key is Key.LeftAlt or Key.RightAlt)
        {
            _altAlone = !e.IsRepeat || _altAlone;
            return;
        }
        _altAlone = false;

        // F10 はシステムキーとして届く。Shift＋F10（右クリックの代わり）は別の操作なので拾わない。
        if (key == Key.F10 && Keyboard.Modifiers == ModifierKeys.None)
        {
            ToggleMenuBar();
            e.Handled = true;
            return;
        }

        // Alt＋アクセスキーで、そのメニューを開く。Alt＋F4 などの OS の組み合わせは文字キーではないので当たらない。
        if (e.Key == Key.System && Keyboard.Modifiers == ModifierKeys.Alt && key is >= Key.A and <= Key.Z)
        {
            var letter = ((char)('A' + (key - Key.A))).ToString();
            if (MenuBarItems.FirstOrDefault(m => string.Equals(m.AccessKey, letter, StringComparison.OrdinalIgnoreCase)) is { } menu)
            {
                menu.Focus();
                menu.OpenWithKeyboard();
                e.Handled = true;
            }
            return;
        }

        if (e.Key != Key.Escape) return;
        // メニューバーの見出しにいて一覧が開いていなければ、メニューバーを出るだけにする（タブを閉じない）。
        if (MenuBarFocused && !MenuBarItems.Any(m => m.IsOpen))
        {
            MenuButton.LeaveMenuBar();
            e.Handled = true;
            return;
        }
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
                Key.Z => IntentKind.UndoRequested,
                Key.Y => IntentKind.RedoRequested,
                Key.F => IntentKind.FocusQueryBoxRequested,
                Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract => IntentKind.ZoomFontRequested,
                _ => null,
            },
            ModifierKeys.Control | ModifierKeys.Shift => key switch
            {
                Key.S => IntentKind.SaveAsRequested,
                Key.Z => IntentKind.RedoRequested,
                // 日本語配列の「＋」は Shift＋「;」で打つので、Shift 付きでも拡大として受ける。
                Key.OemPlus => IntentKind.ZoomFontRequested,
                _ => null,
            },
            _ => null,
        };
        if (kind == IntentKind.ZoomFontRequested)
        {
            this.RaiseIntent(IntentKind.ZoomFontRequested, key is Key.OemMinus or Key.Subtract ? -1 : 1);
            e.Handled = true;
            return;
        }
        // 入力欄の中では、取り消しは入力欄自身の打ち間違いの取り消しに任せる。入力欄に戻せる履歴が
        // 無いときもキーはここまで届くが、辞書を戻すと打っている本人には何が戻ったか分からない。
        if (kind is IntentKind.UndoRequested or IntentKind.RedoRequested && Keyboard.FocusedElement is TextBoxBase { IsReadOnly: false }) return;
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
        System.Windows.Automation.AutomationProperties.SetName(MaximizeRestoreButton, (string)MaximizeRestoreButton.ToolTip);
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
