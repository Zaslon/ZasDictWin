using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

public partial class DockGroupPanel : UserControl
{
    public DockGroupPanel() => InitializeComponent();

    private DockGroup? Group => DataContext as DockGroup;

    /// <summary>タブを押したら前に出す。掴んで運ぶ側（OverlayDrag）とは別に効かせる。</summary>
    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OverlayViewModel vm } && Group is { } group)
            group.Selected = vm;
    }

    // 辺の大きさ。付いている辺と反対向きに引くと広がる（向きは DockGroup 側）。
    private void Grip_DragDelta(object sender, DragDeltaEventArgs e)
        => Group?.Resize(e.HorizontalChange, e.VerticalChange);

    private void Grip_DragCompleted(object sender, DragCompletedEventArgs e)
        => Group?.Persist?.Invoke();
}
