using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ZasDictWin.Views;

public partial class ChangelogPanel : UserControl
{
    public ChangelogPanel() => InitializeComponent();

    /// <summary>
    /// 更新履歴は追記順（古い → 新しい）なので、画面に出た時点で最新行が見えるよう一度だけ末尾へ送る。
    /// </summary>
    private void ChangelogScroll_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer scroll) return;
        scroll.ScrollToEnd();
        // Loaded の時点では高さが確定していないことがあるため、レイアウトが一通り終わった後にもう一度送る。
        scroll.Dispatcher.BeginInvoke(new Action(scroll.ScrollToEnd), DispatcherPriority.Loaded);
    }
}
