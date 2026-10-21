using System.Windows;
using System.Windows.Controls;
using ZasDictWin.Root;

namespace ZasDictWin.Tests;

public class UiIntentTests
{
    private static (Border Top, Border Middle, Border Leaf) Nest()
    {
        var leaf = new Border();
        var middle = new Border { Child = leaf };
        var top = new Border { Child = middle };
        return (top, middle, leaf);
    }

    [Fact]
    public void RaisedFromLeaf_ReachesTop_WithAllFields() => Sta.Run(() =>
    {
        var (top, _, leaf) = Nest();
        UiIntentEventArgs? got = null;
        top.AddIntentHandler((_, e) => got = e);

        var args = new UiIntentEventArgs(IntentKind.PointerMoved, "payload") { ScreenPoint = new Point(3, 4) };
        args.Context.LeafId = 7;
        leaf.RaiseIntent(args);

        Assert.NotNull(got);
        Assert.Equal(IntentKind.PointerMoved, got!.Kind);
        Assert.Equal("payload", got.Payload);
        Assert.Equal(new Point(3, 4), got.ScreenPoint);
        Assert.Equal(7, got.Context.LeafId);
    });

    [Fact]
    public void ScreenPointOverload_SetsScreenPoint() => Sta.Run(() =>
    {
        var (top, _, leaf) = Nest();
        UiIntentEventArgs? got = null;
        top.AddIntentHandler((_, e) => got = e);

        leaf.RaiseIntent(IntentKind.PointerReleased, new Point(10, 20), 5);

        Assert.Equal(new Point(10, 20), got!.ScreenPoint);
        Assert.Equal(5, got.Payload);
    });

    [Fact]
    public void MiddleContext_IsVisibleAtTop() => Sta.Run(() =>
    {
        var (top, middle, leaf) = Nest();
        UiIntentEventArgs? got = null;
        middle.AddIntentHandler((_, e) => e.Context.LeafId ??= 42);
        top.AddIntentHandler((_, e) => got = e);

        leaf.RaiseIntent(IntentKind.TabSelectRequested);

        Assert.Equal(42, got!.Context.LeafId);
    });

    [Fact]
    public void IntentTrigger_RaisesOnClick_WithPayload() => Sta.Run(() =>
    {
        var button = new Button();
        var top = new Border { Child = button };
        UiIntentEventArgs? got = null;
        top.AddIntentHandler((_, e) => got = e);
        IntentTrigger.SetOnClick(button, IntentKind.SaveRequested);
        IntentTrigger.SetPayload(button, "p");

        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.Equal(IntentKind.SaveRequested, got!.Kind);
        Assert.Equal("p", got.Payload);
    });
}
