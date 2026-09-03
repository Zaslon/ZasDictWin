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
    }
}
