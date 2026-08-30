using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
        _vm.SelectionChanged += () => _stream?.Refresh();
        _vm.SettingsApplied += () => _stream?.ApplySettings();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_vm.IsOverlayOpen) { _vm.CloseOverlay(); e.Handled = true; return; }
        if (_vm.IsNavigateLayout && _vm.NavIndex == 1) { _vm.NavIndex = 0; e.Handled = true; }
    }

    // サイドバーの幅ドラッグ。右端に寄せているので左へ引く（負方向）と広がります。
    private void BrowserGrip_DragDelta(object sender, DragDeltaEventArgs e)
        => _vm.Browser.Width -= e.HorizontalChange;

    private void BrowserGrip_DragCompleted(object sender, DragCompletedEventArgs e)
        => _vm.Browser.CommitWidth();

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
        _stream?.Close();
        base.OnClosing(e);
    }
}
