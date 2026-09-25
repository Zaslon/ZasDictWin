using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Views;

public partial class DockSplitPanel : UserControl
{
    public DockSplitPanel() => InitializeComponent();

    private DockSplit? Split => DataContext as DockSplit;

    private void Grip_DragStarted(object sender, DragStartedEventArgs e) => Raise(IntentKind.SplitGripPressed);

    // 境目のつまみ。引いた向きの実寸で割った量がそのまま取り分の変化になる。
    private void Grip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (Split is not { } split) return;
        Raise(IntentKind.PointerMoved, split.IsColumns ? e.HorizontalChange : e.VerticalChange,
            split.IsColumns ? Host.ActualWidth : Host.ActualHeight);
    }

    private void Grip_DragCompleted(object sender, DragCompletedEventArgs e)
        => Raise(e.Canceled ? IntentKind.PointerCaptureLost : IntentKind.PointerReleased);

    private void Raise(IntentKind kind, double? change = null, double? total = null)
    {
        if (Split is not { } split) return;
        var args = new UiIntentEventArgs(kind);
        args.Context.SplitId = split.Id;
        args.Context.DragChange = change;
        args.Context.DragTotal = total;
        this.RaiseIntent(args);
    }
}
