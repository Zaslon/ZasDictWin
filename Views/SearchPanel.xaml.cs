using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ZasDictWin.Models;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

public partial class SearchPanel : UserControl
{
    private MainViewModel? _vm;

    public SearchPanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as MainViewModel);
        // タブを別の枠や独立ウィンドウへ運ぶと器ごと作り直されるので、外れた器は指示を受けないようにする。
        Unloaded += (_, _) => Attach(null);
        Loaded += (_, _) => Attach(DataContext as MainViewModel);
    }

    private void Attach(MainViewModel? vm)
    {
        if (_vm is not null) _vm.FocusRequested -= MoveFocusTo;
        _vm = vm;
        if (_vm is not null) _vm.FocusRequested += MoveFocusTo;
    }

    private void ResultList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // ヒットテスト結果が項目でない（余白のダブルクリック）場合は編集を開かない。
        if (ResultList.SelectedItem is not Word w) return;
        ResultList.RaiseIntent(IntentKind.EditWordRequested, w);
    }

    private void ResultList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, ResultList)) return;
        ResultList.RaiseIntent(IntentKind.SelectWordRequested, ResultList.SelectedItem);
    }

    // Query の束縛は Delay 付きなので、打ち終えてから値が流れ込んだ時点で絞り込みを頼む。
    private void QueryBox_SourceUpdated(object? sender, DataTransferEventArgs e)
        => QueryBox.RaiseIntent(IntentKind.QueryChanged);

    /// <summary>検索欄で Enter を押したら結果一覧へ移る。検索と一覧を Tab 無しで往復できるようにする。
    /// 修飾キー付き（Ctrl+Enter 等）は本体のショートカットに譲るため、素の Enter のみ拾う。</summary>
    private void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        QueryBox.RaiseIntent(IntentKind.FocusResultListRequested);
    }

    /// <summary>結果一覧で Enter を押したら検索欄へ戻る。
    /// 修飾キー付き（Ctrl+Enter 等）は本体のショートカットに譲るため、素の Enter のみ拾う。</summary>
    private void ResultList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        ResultList.RaiseIntent(IntentKind.FocusQueryBoxRequested);
    }

    /// <summary>フォーカスを移す。どちらも WPF の API でしか動かせない手順なのでここに残す。</summary>
    public void MoveFocusTo(FocusTarget target)
    {
        switch (target)
        {
            case FocusTarget.ResultList:
                // Query の束縛は Delay 付きなので、打ち終えた直後の Enter では一覧がまだ古い。
                // 先に値を流し込んで絞り込みを済ませてから、その結果へ移る。
                QueryBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                if (ResultList.Items.Count == 0) return;

                if (ResultList.SelectedIndex < 0) ResultList.SelectedIndex = 0;
                ResultList.ScrollIntoView(ResultList.SelectedItem);
                // 仮想化のため画面外の行にはコンテナが無い。ScrollIntoView の後にレイアウトを進めてから掴む。
                ResultList.UpdateLayout();
                if (ResultList.ItemContainerGenerator.ContainerFromIndex(ResultList.SelectedIndex) is ListBoxItem row) row.Focus();
                else ResultList.Focus();
                break;
            case FocusTarget.QueryBox:
                // 続けて別の語を打てるよう全選択にしておく。
                QueryBox.SelectAll();
                QueryBox.Focus();
                break;
        }
    }

    /// <summary>行の ⋯ は開く直前に組み直す。ListBox は行を使い回す（仮想化のリサイクル）ため、
    /// 行の生成時に詰める作りだと中身が入らないまま開く行が出る。</summary>
    private void RowMenu_Opening(object sender, EventArgs e)
    {
        if (sender is not MenuButton menu) return;
        menu.Items = AppRoot.Current.Mediator.BuildRowMenu(menu.DataContext);
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
