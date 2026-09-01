using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

public partial class DockGroupPanel : UserControl
{
    public DockGroupPanel() => InitializeComponent();

    private DockLeaf? Leaf => DataContext as DockLeaf;

    /// <summary>タブを押したら前に出す。掴んで運ぶ側（OverlayDrag）とは別に効かせる。</summary>
    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OverlayViewModel vm } && Leaf is { } leaf)
            leaf.Selected = vm;
    }

    /// <summary>空の枠を畳む。隣の枠がその場所を引き取る（角を隣へ引く結合と同じ結果）。</summary>
    private void CloseArea_Click(object sender, RoutedEventArgs e)
    {
        if (Leaf is { } leaf) leaf.Owner?.Dissolve(leaf);
    }
}
