using System.Windows;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

public partial class StreamWindow : Window
{
    private readonly MainViewModel _vm;

    public StreamWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        ApplySettings();
    }

    public void Refresh()
    {
        // SelectedWord の変更は MainViewModel の PropertyChanged で伝わるため、
        // ここでは配信側だけの再描画トリガとして残してある。
    }

    /// <summary>
    /// AppSettings は変更通知を出さないため、設定適用時は DataContext を張り直して
    /// 背景色や表示項目のバインディングを一括で読み直す。
    /// </summary>
    public void ApplySettings()
    {
        Topmost = _vm.Settings.StreamWindowTopmost;
        var dc = DataContext;
        DataContext = null;
        DataContext = dc;
    }
}
