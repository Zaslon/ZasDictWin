using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

public partial class DockSplitPanel : UserControl
{
    public DockSplitPanel() => InitializeComponent();

    private DockSplit? Split => DataContext as DockSplit;

    // 境目のつまみ。引いた向きの実寸で割った量がそのまま取り分の変化になる。
    private void Grip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (Split is not { } split) return;
        split.Resize(
            split.IsColumns ? e.HorizontalChange : e.VerticalChange,
            split.IsColumns ? Host.ActualWidth : Host.ActualHeight);
    }

    private void Grip_DragCompleted(object sender, DragCompletedEventArgs e) => Split?.Owner?.Save();
}
