using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

public partial class DockGroupPanel : UserControl
{
    public DockGroupPanel() => InitializeComponent();

    private DockLeaf? Leaf => DataContext as DockLeaf;

    /// <summary>タブを押したら前に出す。掴んで運ぶ側（OverlayDrag）とは別に効かせる。
    /// 中ボタンなら前に出さず、据え置きでなければそのまま閉じる（ブラウザの中クリックと同じ操作感）。</summary>
    private void Tab_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: OverlayViewModel vm } || Leaf is not { } leaf) return;

        if (e.ChangedButton == MouseButton.Middle)
        {
            if (!vm.IsPinned && vm.CloseCommand.CanExecute(null)) vm.CloseCommand.Execute(null);
            return;
        }

        leaf.Selected = vm;
    }

    /// <summary>空の枠を畳む。隣の枠がその場所を引き取る（角を隣へ引く結合と同じ結果）。</summary>
    private void CloseArea_Click(object sender, RoutedEventArgs e)
    {
        if (Leaf is { } leaf) leaf.Owner?.Dissolve(leaf);
    }
}
