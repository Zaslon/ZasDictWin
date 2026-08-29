using System.Windows.Controls;
using System.Windows.Input;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

public partial class SearchPanel : UserControl
{
    public SearchPanel() => InitializeComponent();

    private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // ヒットテスト結果が項目でない（余白のダブルクリック）場合は編集を開かない。
        if (ResultList.SelectedItem is null) return;
        if (DataContext is MainViewModel vm && vm.EditWordCommand.CanExecute(null))
            vm.EditWordCommand.Execute(ResultList.SelectedItem);
    }
}
