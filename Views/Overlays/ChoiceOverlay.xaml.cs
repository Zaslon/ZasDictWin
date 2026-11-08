using System.Windows.Controls;
using System.Windows.Input;

namespace ZasDictWin.Views.Overlays;

public partial class ChoiceOverlay : UserControl
{
    public ChoiceOverlay()
    {
        InitializeComponent();
        // 層が差し替わった直後はまだ描画前で Focus が効かないことがあるので、Loaded を待つ。
        Loaded += (_, _) => Keyboard.Focus(Heading);
    }
}
