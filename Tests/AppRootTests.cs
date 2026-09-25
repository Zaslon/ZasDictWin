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
        public List<IUiHost> Closed { get; } = new();
        public IUiHost OpenFloating(DockFloat host) => throw new NotSupportedException();
        public IUiHost OpenSettings(SettingsViewModel state) => throw new NotSupportedException();
        public IUiHost OpenStream() { Streams++; return new StubHost(HostRole.Stream, Rect.Empty); }
        public IUiHost OpenCount() => throw new NotSupportedException();
        public void Close(IUiHost host) => Closed.Add(host);
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
}
