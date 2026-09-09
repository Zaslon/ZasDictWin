using System.Windows;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

public partial class CountWindow : Window
{
    private readonly MainViewModel _vm;

    public CountWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        ApplySettings();
    }

    /// <summary>
    /// AppSettings は変更通知を出さないため、設定適用時は DataContext を張り直して
    /// 背景色のバインディングを読み直す（StreamWindow と同じ理由）。
    /// </summary>
    public void ApplySettings()
    {
        Topmost = _vm.Settings.StreamWindowTopmost;
        var dc = DataContext;
        DataContext = null;
        DataContext = dc;
    }
}
