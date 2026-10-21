using System.Windows;
using System.Windows.Controls;
using ZasDictWin.Mediator;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Tests;

public class AppRootTests
{
    private sealed class SpyFactory : IHostFactory
    {
        public int Streams { get; private set; }
        public int Counts { get; private set; }
        public List<DockFloat> Floats { get; } = new();
        public List<SettingsViewModel> Settings { get; } = new();
        public List<IUiHost> Closed { get; } = new();
        public IUiHost OpenFloating(DockFloat host) { Floats.Add(host); return new StubHost(HostRole.Floating, Rect.Empty, id: host.Id); }
        public IUiHost OpenSettings(SettingsViewModel state) { Settings.Add(state); return new StubHost(HostRole.Settings, Rect.Empty); }
        public IUiHost OpenStream() { Streams++; return new StubHost(HostRole.Stream, Rect.Empty); }
        public IUiHost OpenCount() { Counts++; return new StubHost(HostRole.Count, Rect.Empty); }
        public void Close(IUiHost host) => Closed.Add(host);
    }

    /// <summary>DropDown / MenuButton と同じく、畳まれたら閉じた知らせを木から上げる。</summary>
    private sealed class SpyPopup : IPopupLayer
    {
        private readonly UIElement? _tree;
        public SpyPopup(UIElement? tree = null) => _tree = tree;
        public int Closed { get; private set; }

        public void Close()
        {
            Closed++;
            _tree?.RaiseIntent(IntentKind.PopupLayerClosed, this);
        }
    }

    /// <summary>本体の窓を 1 つ載せ、Intent をその木から上げられるようにする。</summary>
    private static (AppRoot Root, SpyFactory Factory, Border Tree) WithShell()
    {
        var (root, factory, _) = NewRoot();
        var tree = new Border();
        root.Attach(new StubHost(HostRole.Shell, new Rect(0, 0, 1000, 800)), tree);
        tree.RaiseIntent(IntentKind.WindowActivated);
        return (root, factory, tree);
    }

    private static (AppRoot Root, SpyFactory Factory, List<StateChange> Changes) NewRoot()
    {
        var services = new StubServices();
        var state = new MainViewModel(services.Settings, () => { });
        var root = new AppRoot(new AppMediator(services, state, state.Layout));
        var factory = new SpyFactory();
        root.HostFactory = factory;
        var changes = new List<StateChange>();
        root.Mediator.Changed += changes.Add;
        return (root, factory, changes);
    }

    [Fact]
    public void AttachedTree_DispatchesOnceWithHostId() => Sta.Run(() =>
    {
        var (root, factory, changes) = NewRoot();
        var host = new StubHost(HostRole.Shell, new Rect(0, 0, 100, 100));
        var child = new Border();
        var tree = new Border { Child = child };
        root.Attach(host, tree);

        var args = new UiIntentEventArgs(IntentKind.StreamWindowToggleRequested);
        child.RaiseIntent(args);

        Assert.Single(changes);
        Assert.Equal(host.HostId, args.Context.HostId);
        Assert.True(args.Handled);
        Assert.Equal(1, factory.Streams);
    });

    [Fact]
    public void Detached_DoesNotDispatch() => Sta.Run(() =>
    {
        var (root, _, changes) = NewRoot();
        var host = new StubHost(HostRole.Shell, new Rect(0, 0, 100, 100));
        var tree = new Border();
        root.Attach(host, tree);
        root.Detach(host);

        tree.RaiseIntent(IntentKind.CancelRequested);

        Assert.Empty(changes);
        Assert.Empty(root.Hosts);
    });

    [Fact]
    public void DoubleAttach_DispatchesOnce() => Sta.Run(() =>
    {
        var (root, _, changes) = NewRoot();
        var host = new StubHost(HostRole.Shell, new Rect(0, 0, 100, 100));
        var tree = new Border();
        root.Attach(host, tree);
        root.Attach(host, tree);

        tree.RaiseIntent(IntentKind.CancelRequested);

        Assert.Single(changes);
        Assert.Single(root.Hosts);
    });

    [Fact]
    public void HitTest_PrefersActiveFloatingThenFloatingThenShell() => Sta.Run(() =>
    {
        var (root, _, _) = NewRoot();
        var shell = new StubHost(HostRole.Shell, new Rect(0, 0, 1000, 1000), leafId: 1);
        var inactive = new StubHost(HostRole.Floating, new Rect(100, 100, 300, 300), leafId: 2);
        var active = new StubHost(HostRole.Floating, new Rect(150, 150, 300, 300), leafId: 3, active: true);
        var settings = new StubHost(HostRole.Settings, new Rect(0, 0, 2000, 2000));
        root.Attach(settings, new Border());
        root.Attach(shell, new Border());
        root.Attach(inactive, new Border());
        root.Attach(active, new Border());

        Assert.Same(active, root.HitTest(new Point(200, 200))!.Value.Host);
        Assert.Same(inactive, root.HitTest(new Point(120, 120))!.Value.Host);
        Assert.Same(shell, root.HitTest(new Point(900, 900))!.Value.Host);
        Assert.Null(root.HitTest(new Point(1500, 1500)));
    });

    [Fact]
    public void HitTest_OnWindowButOffLeaf_HasNoLeaf() => Sta.Run(() =>
    {
        var (root, _, _) = NewRoot();
        var shell = new StubHost(HostRole.Shell, new Rect(0, 0, 1000, 1000), leafId: -1);
        root.Attach(shell, new Border());

        var hit = root.HitTest(new Point(10, 10));
        Assert.NotNull(hit);
        Assert.False(hit!.Value.HasLeaf);
    });

    [Fact]
    public void ToggleStreamOff_ClosesTheStreamHost() => Sta.Run(() =>
    {
        var (root, factory, _) = NewRoot();
        var shell = new StubHost(HostRole.Shell, new Rect(0, 0, 100, 100));
        var stream = new StubHost(HostRole.Stream, new Rect(0, 0, 100, 100));
        var tree = new Border();
        root.Attach(shell, tree);
        root.Attach(stream, new Border());

        tree.RaiseIntent(IntentKind.StreamWindowToggleRequested);   // 開く（すでに窓があるので作らない）
        tree.RaiseIntent(IntentKind.StreamWindowToggleRequested);   // 閉じる

        Assert.Equal(0, factory.Streams);
        Assert.Equal(new IUiHost[] { stream }, factory.Closed);
    });

    [Fact]
    public void ToggleCount_OpensCountHost() => Sta.Run(() =>
    {
        var (_, factory, tree) = WithShell();
        tree.RaiseIntent(IntentKind.CountWindowToggleRequested);
        Assert.Equal(1, factory.Counts);
    });

    [Fact]
    public void Esc_ClosesThePopupThatWasOpened() => Sta.Run(() =>
    {
        var (_, _, tree) = WithShell();
        var popup = new SpyPopup();

        tree.RaiseIntent(IntentKind.PopupLayerOpened, popup);
        tree.RaiseIntent(IntentKind.CancelRequested);

        Assert.Equal(1, popup.Closed);
    });

    [Fact]
    public void OpeningSecondPopup_ClosesFirst_ThenEscClosesSecond() => Sta.Run(() =>
    {
        var (_, _, tree) = WithShell();
        var first = new SpyPopup(tree);
        var second = new SpyPopup(tree);

        tree.RaiseIntent(IntentKind.PopupLayerOpened, first);
        // 裁定の中で前の一覧が畳まれ、その閉じた知らせも裁定の中で届く。今開いた方は裁定の後で覚える。
        tree.RaiseIntent(IntentKind.PopupLayerOpened, second);
        Assert.Equal((1, 0), (first.Closed, second.Closed));

        tree.RaiseIntent(IntentKind.CancelRequested);
        Assert.Equal((1, 1), (first.Closed, second.Closed));
    });

    [Fact]
    public void PopupClosedByUser_IsNotClosedAgainByEsc() => Sta.Run(() =>
    {
        var (_, _, tree) = WithShell();
        var popup = new SpyPopup(tree);

        tree.RaiseIntent(IntentKind.PopupLayerOpened, popup);
        tree.RaiseIntent(IntentKind.PopupLayerClosed, popup);
        tree.RaiseIntent(IntentKind.CancelRequested);

        Assert.Equal(0, popup.Closed);
    });

    [Fact]
    public void ReopeningTool_FocusesItsWindow() => Sta.Run(() =>
    {
        var (root, factory, tree) = WithShell();
        tree.RaiseIntent(IntentKind.OpenScreenRequested, AppCommand.ShowIpaTool);
        var floating = Assert.Single(factory.Floats);
        var window = new StubHost(HostRole.Floating, new Rect(0, 0, 100, 100), id: floating.Id);
        root.Attach(window, new Border());

        tree.RaiseIntent(IntentKind.OpenScreenRequested, AppCommand.ShowIpaTool);

        Assert.Equal(1, window.Focused);
        Assert.Single(factory.Floats);
    });

    [Fact]
    public void SettingsRequest_OpensSettingsWindow() => Sta.Run(() =>
    {
        var (_, factory, tree) = WithShell();
        tree.RaiseIntent(IntentKind.SettingsRequested);
        Assert.Single(factory.Settings);
    });

    [Fact]
    public void ViewCommands_AreForwardedToPresenters() => Sta.Run(() =>
    {
        var (root, _, tree) = WithShell();
        var seen = new List<HostCommand>();
        root.ViewCommandIssued += seen.Add;

        tree.RaiseIntent(IntentKind.FocusQueryBoxRequested);

        Assert.Contains(new HostCommand.MoveFocus(FocusTarget.QueryBox), seen);
    });
}
