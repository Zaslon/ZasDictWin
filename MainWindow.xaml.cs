using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private StreamWindow? _stream;
    private bool _forceClose;

    // 窓の外へ持ち出したタブ。中身のある浮き枠ひとつにつき 1 枚の窓を開ける。
    // 割り付け（DockLayout）が正で、ここはその通知を受けて窓を合わせるだけ。
    private readonly Dictionary<DockFloat, FloatingWindow> _floats = new();
    private bool _syncingFloats;
    private bool _shuttingDown;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        // 選択の変更は MainViewModel の PropertyChanged で単語ウィンドウにも届く。
        // 設定だけは AppSettings が変更通知を持たないので、明示的に張り直させる。
        _vm.SettingsApplied += () => _stream?.ApplySettings();
        _vm.PropertyChanged += Vm_PropertyChanged;
        App.UiException += ShowException;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;

        // バーが手狭なので、開く・新規辞書・保存・別名で保存は階層メニューにまとめてある。
        // Command は ViewModel の RelayCommand をそのまま渡すだけなので、Binding は使わずここで詰める。
        FileMenuButton.Items = new[]
        {
            new MenuAction { Header = "開く", ToolTip = "Ctrl+O", Command = _vm.OpenCommand },
            new MenuAction { Header = "新規辞書", Command = _vm.NewDictionaryCommand },
            new MenuAction { Header = "保存", ToolTip = "Ctrl+S", Command = _vm.SaveCommand, IsPrimary = true },
            new MenuAction { Header = "別名で保存", ToolTip = "Ctrl+Shift+S", Command = _vm.SaveAsCommand },
        };

        // 統計・凡例・更新履歴・方言変換・IPA→綴りは、常設のタブ枠を割かないよう独立ウィンドウで開く。
        // ただし中身は他と同じタブなので、掴んで本体の枠へ運べばタブになる。
        // すでに開いていれば作り直さず、そのタブを表に出すだけ（ボタンはグレーアウトさせない）。
        ToolsMenuButton.Items = new[]
        {
            new MenuAction { Header = "変換", Command = _vm.ShowDialectToolCommand },
            new MenuAction { Header = "IPA", Command = _vm.ShowIpaToolCommand },
            new MenuAction { Header = "統計", Command = _vm.ShowStatsCommand },
            new MenuAction { Header = "凡例", Command = _vm.ShowLegendCommand },
            new MenuAction { Header = "更新履歴", Command = _vm.ShowChangelogCommand },
        };

        // 独立ウィンドウは割り付けが持つ浮き枠と 1 対 1。中身が入れば開き、空になれば閉じる。
        _vm.Layout.FloatsChanged += SyncFloatWindows;
        _vm.OverlayFocused += FocusOverlay;
        // 保存した割り付けに独立ウィンドウが含まれていれば、本体が出た後に開く
        // （Owner を持たせるため、本体に HWND ができてからでないと開けない）。
        Loaded += (_, _) => SyncFloatWindows();

        // 枠を消したので OS は大きさを覚えてくれない。前回閉じたときの大きさをここで復元する。
        Width = _vm.Settings.WindowWidth;
        Height = _vm.Settings.WindowHeight;
        if (_vm.Settings.WindowMaximized) WindowState = WindowState.Maximized;
        StateChanged += (_, _) => UpdateMaximizeRestoreIcon();
        UpdateMaximizeRestoreIcon();
    }

    // UI スレッドで漏れた例外はアプリを落とさず、OBS に映るオーバーレイで知らせます。
    // MessageBox は別ウィンドウになるため使わない方針です。
    private void ShowException(Exception ex)
    {
        try
        {
            var vm = new ChoiceViewModel(
                "問題が発生しました",
                $"この操作は中止しましたが、アプリはそのまま続けられます。\n\n" +
                $"{ex.GetType().Name}: {ex.Message}\n\n" +
                $"詳しい記録: {ErrorLog.FilePath}");
            vm.AddCancel("閉じる");
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

    // 枠を消した代わりに、ヘッダの余白（ボタンの無い部分）をドラッグでの移動とダブルクリックでの
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
        MaximizeRestoreButton.ToolTip = maximized ? "元のサイズに戻す" : "最大化";
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

    private void ToggleStreamWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_stream is not null)
        {
            _stream.Close();
            return;
        }

        // 独立した HWND にするため Owner は設定しない。OBS 側で個別のウィンドウ
        // キャプチャソースとして選べる必要がある。
        _stream = new StreamWindow(_vm);
        _stream.Closed += (_, _) => { _stream = null; StreamButton.Content = "単語ウィンドウ"; };
        _stream.Show();
        StreamButton.Content = "単語ウィンドウを閉じる";
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
