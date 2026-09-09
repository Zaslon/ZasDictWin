using System.Windows.Controls;
using System.Windows.Input;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views.Overlays;

public partial class WordEditOverlay : UserControl
{
    // 開いた直後にクリック無しで入力を始められるよう、見出し語欄へ最初のフォーカスを置く。
    public WordEditOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) => FormBox.Focus();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // 「内容」欄は AcceptsReturn なので素の Enter を改行として消費してしまう。
    // Preview（＝子要素より先に見る段）で Ctrl+Enter だけを拾い、保存ボタン相当として扱う。
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.Control) return;
        if (DataContext is not WordEditViewModel vm) return;
        if (vm.SaveCommand.CanExecute(null)) vm.SaveCommand.Execute(null);
        e.Handled = true;
    }
}
