using System.Windows.Controls;
using System.Windows.Input;

namespace ZasDictWin.Views.Overlays;

public partial class DisplaySettingsOverlay : UserControl
{
    public DisplaySettingsOverlay()
    {
        InitializeComponent();
        // 確認ダイアログと同じく、開いたときのフォーカスは見出しに置き、支援技術には題から読ませる。
        // 層が差し替わった直後はまだ描画前で Focus が効かないことがあるので、Loaded を待つ。
        Loaded += (_, _) => Keyboard.Focus(Heading);
    }
}
