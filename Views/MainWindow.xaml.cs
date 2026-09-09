using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using ZasDictWin.Resources;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private StreamWindow? _stream;
    private CountWindow? _count;
    private SettingsWindow? _settings;
    private bool _forceClose;

    // 窓の外へ持ち出したタブ。中身のある浮き枠ひとつにつき 1 枚の窓を開ける。
    // 割り付け（DockLayout）が正で、ここはその通知を受けて窓を合わせるだけ。
    private readonly Dictionary<DockFloat, FloatingWindow> _floats = new();
    private bool _syncingFloats;
    private bool _shuttingDown;

    // 縦横比固定モードの間だけ値を持つ（幅 / 高さ）。null なら WndProc は何もしない。
    // 比率は設定を適用した瞬間の幅・高さから決まり、以後は端をつまんだリサイズがこれを崩さない。
    private double? _aspectRatio;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        // 選択や語数の変更は MainViewModel の PropertyChanged で配信用ウィンドウにも届く。
        // 設定だけは AppSettings が変更通知を持たないので、明示的に張り直させる。
        _vm.SettingsApplied += () => { _stream?.ApplySettings(); _count?.ApplySettings(); ApplyWindowSizeSettings(); };
        _vm.SettingsRequested += ShowSettingsWindow;
        _vm.PropertyChanged += Vm_PropertyChanged;
        App.UiException += ShowException;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;

        // バーが手狭なので、開く・新規辞書・保存・別名で保存は階層メニューにまとめてある。
        // Command は ViewModel の RelayCommand をそのまま渡すだけなので、Binding は使わずここで詰める。
        FileMenuButton.Items = new[]
        {
            new MenuAction { Header = Strings.Menu_Open, ToolTip = "Ctrl+O", Command = _vm.OpenCommand },
            new MenuAction { Header = Strings.Menu_NewDictionary, Command = _vm.NewDictionaryCommand },
            new MenuAction { Header = Strings.Common_Save, ToolTip = "Ctrl+S", Command = _vm.SaveCommand, IsPrimary = true },
            new MenuAction { Header = Strings.Menu_SaveAs, ToolTip = "Ctrl+Shift+S", Command = _vm.SaveAsCommand },
        };

        // 統計・凡例・更新履歴・方言変換・IPA→綴りは、常設のタブ枠を割かないよう独立ウィンドウで開く。
        // ただし中身は他と同じタブなので、掴んで本体の枠へ運べばタブになる。
        // すでに開いていれば作り直さず、そのタブを表に出すだけ（ボタンはグレーアウトさせない）。
        ToolsMenuButton.Items = new[]
        {
            new MenuAction { Header = Strings.Dialect_Title, Command = _vm.ShowDialectToolCommand },
            new MenuAction { Header = Strings.Ipa_Title, Command = _vm.ShowIpaToolCommand },
            new MenuAction { Header = Strings.Stats_Title, Command = _vm.ShowStatsCommand },
            new MenuAction { Header = Strings.Legend_Title, Command = _vm.ShowLegendCommand },
            new MenuAction { Header = Strings.Changelog_Title, Command = _vm.ShowChangelogCommand },
        };

        // 配信用の独立ウィンドウ。項目名が開閉で変わるので、開く直前に組み直す。
        WindowMenuButton.Opening += (_, _) => BuildWindowMenu();
        BuildWindowMenu();

        // 独立ウィンドウは割り付けが持つ浮き枠と 1 対 1。中身が入れば開き、空になれば閉じる。
        _vm.Layout.FloatsChanged += SyncFloatWindows;
        _vm.OverlayFocused += FocusOverlay;
        // 保存した割り付けに独立ウィンドウが含まれていれば、本体が出た後に開く
        // （Owner を持たせるため、本体に HWND ができてからでないと開けない）。
        Loaded += (_, _) => SyncFloatWindows();

        // 標準の枠が無いので OS は大きさを覚えてくれない。前回閉じたときの大きさをここで復元する。
        Width = _vm.Settings.WindowWidth;
        Height = _vm.Settings.WindowHeight;
        if (_vm.Settings.WindowMaximized) WindowState = WindowState.Maximized;
        if (_vm.Settings.WindowAspectLocked) _aspectRatio = _vm.Settings.WindowWidth / _vm.Settings.WindowHeight;
        StateChanged += (_, _) => UpdateMaximizeRestoreIcon();
        UpdateMaximizeRestoreIcon();
    }

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

    /// <summary>
    /// 設定を適用した直後に呼ぶ。最大化中は Width/Height が画面いっぱいの値になっており
    /// ここで上書きすると復元後の大きさが狂うため、通常時だけ実際の大きさへ反映する。
    /// 固定する比率は「その時点の設定値」から決め直す（既存の窓の実寸ではなく、
    /// 設定欄に入っている幅・高さを比率の基準にする）。
    /// </summary>
    private void ApplyWindowSizeSettings()
    {
        var s = _vm.Settings;
        if (WindowState == WindowState.Normal)
        {
            Width = s.WindowWidth;
            Height = s.WindowHeight;
        }
        _aspectRatio = s.WindowAspectLocked ? s.WindowWidth / s.WindowHeight : null;
    }

    // UI スレッドで漏れた例外はアプリを落とさず、OBS に映るオーバーレイで知らせます。
    // MessageBox は別ウィンドウになるため使わない方針です。
    private void ShowException(Exception ex)
    {
        try
        {
            var vm = new ChoiceViewModel(
                Strings.Error_Title,
                string.Format(Strings.Error_Message, ex.GetType().Name, ex.Message, ErrorLog.FilePath));
            vm.AddCancel(Strings.Common_Close);
            _vm.ShowOverlay(vm);
        }
        catch (Exception overlayEx)
        {
            // オーバーレイを描くこと自体が失敗する状態ではこれ以上出さず、記録だけ残します。
            ErrorLog.Write("ErrorOverlay", overlayEx);
        }
    }

    // Status は差分の無い書き換え（同じ文言の再設定）でも起きうるが、ここでは変化に気づかせることが
    // 目的なので毎回律儀に光らせる。連続で変わっても Storyboard.Begin は前回分を上書きするだけで済む。
    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Status)) return;
        ((Storyboard)Resources["StatusFlashStoryboard"]).Begin(StatusFlashBorder);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        // プルダウンを開いている間は、Esc をオーバーレイごと閉じる操作に使わせない。
        // ここは Preview（＝ウィンドウが最初に見る段）なので、先に一覧だけを畳む。
        if (DropDown.CloseCurrent()) { e.Handled = true; return; }
        if (MenuButton.CloseCurrent()) { e.Handled = true; return; }
        // 枠を割り直している最中と、タブの運び先を選んでいる最中は、まずその操作だけをやめる。
        if (AreaDrag.Cancel()) { e.Handled = true; return; }
        if (OverlayDrag.Cancel()) { e.Handled = true; return; }
        if (RowDrag.Cancel()) { e.Handled = true; return; }
        // 確認は他の画面の上に重なるので、上の層から順に閉じる。
        if (_vm.ModalOverlay is not null) { _vm.CloseModal(); e.Handled = true; return; }
        // 複数開いていても閉じる相手は 1 枚。最後に触ったタブから畳む。
        if (_vm.ActiveOverlay is { } active) { _vm.CloseOverlay(active); e.Handled = true; }
    }

    // Ctrl＋ホイールで文字サイズを増減する。Preview（＝ウィンドウが最初に見る段）で拾って畳むので、
    // ホイールを食う一覧・本文（ScrollViewer や選択できる本文）の上でも同じように効く。
    // ブラウザのタブ（WebView2）は別 HWND なのでここには届かず、WebView2 自身の拡大縮小が働く。
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || e.Delta == 0) return;
        // 目盛りの大きさ（Delta）は機種差があるので、向きだけを見て 1 段ずつ動かす。
        _vm.ZoomFont(Math.Sign(e.Delta));
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
        MaximizeRestoreButton.ToolTip = maximized ? Strings.Common_Restore : Strings.Common_Maximize;
    }

    // ---- 独立ウィンドウ（窓の外へ持ち出したタブ）----------------------------------------

    /// <summary>
    /// 割り付けの浮き枠に窓を合わせる。中身が入った浮き枠には窓を開け、空になった浮き枠と
    /// 消えた浮き枠の窓は閉じる。窓を閉じると中のタブが動いてここへ戻ってくるので、入れ子は 1 度目に任せる。
    /// </summary>
    private void SyncFloatWindows()
    {
        if (_syncingFloats || _shuttingDown || !IsLoaded) return;
        _syncingFloats = true;
        try
        {
            foreach (var host in _vm.Layout.Floats.ToList())
            {
                var open = _floats.TryGetValue(host, out var window);
                if (host.HasItems && !open) Open(host);
                else if (!host.HasItems && open) Close(host, window!);
            }
            // 割り付けから外れた浮き枠（窓ごと畳んだ枠）の窓も閉じる。
            foreach (var (host, window) in _floats.Where(p => !_vm.Layout.Floats.Contains(p.Key)).ToList())
                Close(host, window);
        }
        finally
        {
            _syncingFloats = false;
        }

        void Open(DockFloat host)
        {
            // Owner を持たせて本体より手前に置く。タブの運び先の当たり判定もこの前後関係を前提にしている。
            var window = new FloatingWindow(_vm, host) { Owner = this };
            _floats[host] = window;
            window.Closing += (_, _) => FloatWindowClosing(host);
            window.Show();
        }

        void Close(DockFloat host, FloatingWindow window)
        {
            // 先に台帳から外す。窓を閉じた通知でここへ戻ってきても、もう閉じにこないようにする。
            _floats.Remove(host);
            window.CloseFromLayout();
        }
    }

    /// <summary>
    /// 独立ウィンドウを手で閉じたとき。中のタブは閉じ、閉じられない据え置きのタブ（検索・単語詳細）は
    /// 本体へ引き取る。割り付け側から閉じた窓は始末が済んでいるので何もしない。
    /// </summary>
    private void FloatWindowClosing(DockFloat host)
    {
        if (_shuttingDown || !_floats.Remove(host)) return;
        // タブを 1 枚閉じるごとに割り付けの通知が飛ぶが、まだ中身の残っている枠を見て
        // 窓を開け直されては困る。始末が終わってから 1 度だけ合わせる。
        _syncingFloats = true;
        try
        {
            foreach (var vm in host.Items.ToList())
            {
                if (!vm.IsPinned) _vm.CloseOverlay(vm);
            }
            // 残った据え置きのタブは Discard が本体へ移す。位置の記憶ごと浮き枠を落とす。
            _vm.Layout.Discard(host);
        }
        finally
        {
            _syncingFloats = false;
        }
        SyncFloatWindows();
    }

    /// <summary>そのタブがいる窓を前に出す。ツールメニューで開き直したときの行き先。</summary>
    private void FocusOverlay(OverlayViewModel vm)
    {
        if (_vm.Layout.FloatOf(vm) is { } host && _floats.TryGetValue(host, out var window)) window.Activate();
        else Activate();
    }

    // ---- 設定ポップアップ（独立ウィンドウ）------------------------------------------------

    /// <summary>
    /// 設定は他のオーバーレイと違い中央モーダルの見た目のまま独立ウィンドウで開く。ShowSettingsCommand
    /// の CanExecute は NoModal だけなので、二重に開かないための判定はここで持つ。
    /// </summary>
    private void ShowSettingsWindow(SettingsViewModel vm)
    {
        if (_settings is not null) { _settings.Activate(); return; }
        _settings = new SettingsWindow(_vm, vm) { Owner = this };
        _settings.Closed += (_, _) => _settings = null;
        // Owner（本体）だけを操作不能にする。単語ウィンドウ・単語数ウィンドウは別 HWND なので影響されない。
        _settings.ShowDialog();
    }

    // ---- 配信用の独立ウィンドウ（単語・単語数）------------------------------------------

    /// <summary>開いているものは「閉じる」に変えて出す。開くたびに組み直すのは、
    /// MenuAction を作った時点の状態で固まってしまわないようにするため。</summary>
    private void BuildWindowMenu()
    {
        WindowMenuButton.Items = new[]
        {
            new MenuAction
            {
                Header = _stream is null ? Strings.Window_StreamMenuOpen : Strings.Window_StreamMenuClose,
                ToolTip = Strings.Window_StreamMenuTooltip,
                Command = new RelayCommand(ToggleStreamWindow),
            },
            new MenuAction
            {
                Header = _count is null ? Strings.Window_CountMenuOpen : Strings.Window_CountMenuClose,
                ToolTip = Strings.Window_CountMenuTooltip,
                Command = new RelayCommand(ToggleCountWindow),
            },
        };
    }

    private void ToggleStreamWindow()
    {
        if (_stream is not null)
        {
            _stream.Close();
            return;
        }

        // 独立した HWND にするため Owner は設定しない。OBS 側で個別のウィンドウ
        // キャプチャソースとして選べる必要がある。
        _stream = new StreamWindow(_vm);
        _stream.Closed += (_, _) => _stream = null;
        _stream.Show();
    }

    private void ToggleCountWindow()
    {
        if (_count is not null)
        {
            _count.Close();
            return;
        }

        _count = new CountWindow(_vm);
        _count.Closed += (_, _) => _count = null;
        _count.Show();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose && !_vm.ConfirmCloseIfDirty(() => { _forceClose = true; Close(); }))
        {
            e.Cancel = true;
            return;
        }
        // ここから先で閉じる独立ウィンドウは「畳んだ」のではなく終了なので、
        // 中のタブを本体へ移し替えない（せっかくの割り付けが保存直前に崩れてしまう）。
        _shuttingDown = true;
        SaveWindowBounds();
        _stream?.Close();
        _count?.Close();
        _settings?.Close();
        base.OnClosing(e);
    }

    // 最大化中は Width/Height が画面いっぱいの値になるため、次に元へ戻したときの大きさが
    // 分かるよう RestoreBounds（最小化・最大化する前の通常時の矩形）を使う。最小化したまま
    // 閉じた場合も同様（RestoreBounds はその前の通常時の矩形を保ったまま）。
    private void SaveWindowBounds()
    {
        var settings = _vm.Settings;
        if (WindowState == WindowState.Normal)
        {
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
            settings.WindowMaximized = false;
        }
        else
        {
            settings.WindowWidth = RestoreBounds.Width;
            settings.WindowHeight = RestoreBounds.Height;
            settings.WindowMaximized = WindowState == WindowState.Maximized;
        }
        settings.Save();
        // 独立ウィンドウの位置と大きさは動かすたび浮き枠に控えてあるだけなので、ここで書き出す。
        _vm.Layout.Save();
    }
}
