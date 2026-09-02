using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ZasDictWin.Models;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

public partial class SearchPanel : UserControl
{
    public SearchPanel() => InitializeComponent();

    private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // ヒットテスト結果が項目でない（余白のダブルクリック）場合は編集を開かない。
        if (ResultList.SelectedItem is not Word w) return;
        if (DataContext is MainViewModel vm) vm.RequestEditWord(w);
    }

    /// <summary>行の ⋯ の中身を、開く直前にその行の単語で組む。ListBox は行を使い回す
    /// （仮想化のリサイクル）ため、行の生成時に詰める作りだと中身が入らないまま開く行が出る。</summary>
    private void RowMenu_Opening(object sender, EventArgs e)
    {
        if (sender is not MenuButton menu) return;

        menu.Items = DataContext is MainViewModel vm && menu.DataContext is Word w
            ? new[]
            {
                new MenuAction { Header = "編集", Command = vm.EditWordCommand, CommandParameter = w },
                new MenuAction { Header = "複製", Command = vm.DuplicateWordCommand, CommandParameter = w },
                new MenuAction { Header = "削除", Command = vm.DeleteWordCommand, CommandParameter = w },
            }
            : null;
    }

    /// <summary>行（ListBoxItem）のどこを右クリックしても、その行の ⋯ を探して同じメニューを開く。</summary>
    private void Row_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DependencyObject root) return;
        if (FindDescendant<MenuButton>(root) is { } menu) menu.Open();
        e.Handled = true;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } found) return found;
        }
        return null;
    }
}
