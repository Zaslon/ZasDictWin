using System.Windows;
using System.Windows.Input;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

/// <summary>
/// 設定ポップアップ。中身はタブ束に混ぜず中央モーダルとして 1 枚だけ出す方針は変わらないが、
/// 器は本体（MainWindow）とは別の独立ウィンドウにしてある。Owner を本体にした ShowDialog で開き、
/// 閉じ方（キャンセル・適用・×・Esc）はすべて SettingsViewModel.RequestClose 経由でここへ集める。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly MainViewModel _main;

    public SettingsWindow(MainViewModel main, SettingsViewModel vm)
    {
        InitializeComponent();
        _main = main;
        DataContext = vm;
        vm.RequestClose = Close;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Close();
        e.Handled = true;
    }

    // 文字サイズ倍率はアプリ全体で 1 つなので、この窓で回しても本体と同じ値を動かす。
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || e.Delta == 0) return;
        _main.ZoomFont(Math.Sign(e.Delta));
        e.Handled = true;
    }

    // 標準の枠が無いので、ヘッダの余白をドラッグでの移動に使う（MainWindow.Header_MouseLeftButtonDown と同じ理由）。
    // 設定は大きさが決め打ちの用途ではないため最大化トグルは持たせていない。
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
