using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using ZasDictWin.Mediator;
using ZasDictWin.Presenters;
using ZasDictWin.Root;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Tests;

public class PresenterTests
{
    /// <summary>Mediator に Root を組み、Presenter が受ける ViewCommandIssued まで通す。</summary>
    private static (MediatorHarness H, AppRoot Root) Setup(Action<StubServices>? setup = null)
    {
        var h = new MediatorHarness(setup);
        var root = new AppRoot(h.M);
        // Root は窓の引き当てを自分の一覧に向け直すので、記録用の窓へ戻す。
        h.M.HostLookup = id => h.Hosts.FirstOrDefault(x => x.HostId == id);
        return (h, root);
    }

    // ---- ShellPresenter ------------------------------------------------------------

    [Fact]
    public void Shell_ButtonsFollowEveryIntent()
    {
        var (h, root) = Setup();
        _ = new ShellPresenter(h.M, h.State, root);
        Assert.False(h.State.CanSave);
        Assert.True(h.State.CanOpen);

        h.OpenDictionary(MediatorHarness.NewWord(1, "a"));

        Assert.True(h.State.CanSave);
        Assert.True(h.State.CanNewWord);
        Assert.True(h.State.CanShowExamples);
    }

    [Fact]
    public void Shell_ConfirmIsShownAndCleared()
    {
        var (h, root) = Setup();
        _ = new ShellPresenter(h.M, h.State, root);
        var a = MediatorHarness.NewWord(1, "a");
        h.OpenDictionary(a);

        h.Services.NextOpenPath = "missing.json";
        h.Send(IntentKind.OpenDictionaryRequested);
        Assert.IsType<ChoiceViewModel>(h.State.ModalOverlay);

        h.Send(IntentKind.ModalDismissRequested);
        Assert.Null(h.State.ModalOverlay);
    }

    [Fact]
    public void Shell_PopupLayerHidesBrowserAndFocusIsForwarded()
    {
        var (h, root) = Setup();
        _ = new ShellPresenter(h.M, h.State, root);
        FocusTarget? focused = null;
        h.State.FocusRequested += t => focused = t;

        h.Send(IntentKind.PopupLayerOpened);
        Assert.True(h.State.Browser.IsOverlayOpen);
        h.Send(IntentKind.PopupLayerClosed);
        Assert.False(h.State.Browser.IsOverlayOpen);

        h.Send(IntentKind.FocusQueryBoxRequested);
        Assert.Equal(FocusTarget.QueryBox, focused);
    }

    [Fact]
    public void Shell_UnchangedMenusAreNotReplaced()
    {
        // 差し替えると開いている一覧が畳まれる。
        var (h, root) = Setup();
        _ = new ShellPresenter(h.M, h.State, root);
        var file = h.State.FileMenuItems;
        var window = h.State.WindowMenuItems;

        h.Send(IntentKind.StreamWindowToggleRequested);

        Assert.Same(file, h.State.FileMenuItems);
        Assert.NotSame(window, h.State.WindowMenuItems);
    }

    // ---- StreamPresenter -------------------------------------------------------------

    [Fact]
    public void Stream_CopiesSettingsAndScalesSizes()
    {
        var (h, root) = Setup(s =>
        {
            s.Settings.StreamFontScale = 2;
            s.Settings.StreamBackground = "#000000";
            s.Settings.StreamWindowTopmost = false;
            s.Settings.StreamShowContents = true;
        });

        var view = new StreamPresenter(h.M, h.State, root).View;

        Assert.Equal(("#000000", false, true), (view.Background, view.IsTopmost, view.ShowContents));
        Assert.Equal((60.0, 30.0, 24.0, 44.0), (view.HeadwordSize, view.BodySize, view.LabelSize, view.PlaceholderSize));
    }

    [Fact]
    public void Stream_FollowsSelectionWithoutWaitingForIntent()
    {
        var (h, root) = Setup();
        var view = new StreamPresenter(h.M, h.State, root).View;
        var a = MediatorHarness.NewWord(1, "a");
        h.OpenDictionary(a);

        h.State.SelectedWord = a;

        Assert.Same(a, view.SelectedWord);
        Assert.True(view.HasSelection);
        Assert.Equal(h.State.WordCountLabel, view.WordCountLabel);
    }

    [Fact]
    public void Stream_SettingsApplyRefreshesStreamWindows()
    {
        var (h, root) = Setup();
        var view = new StreamPresenter(h.M, h.State, root).View;
        h.Send(IntentKind.SettingsRequested);
        var form = Assert.IsType<HostCommand.OpenSettings>(h.HostCommands.Last()).State;
        form.StreamFontScale = 3;

        h.Send(IntentKind.SettingsApplyRequested, from: h.AddHost(HostRole.Settings));

        Assert.Equal(90.0, view.HeadwordSize);
    }

    [Fact]
    public void Stream_LegendIsRenderedAtCurrentFontScale() => Sta.Run(() =>
    {
        var (h, root) = Setup();
        _ = new StreamPresenter(h.M, h.State, root);
        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowLegend);
        var legend = h.Open<LegendViewModel>()!;
        try
        {
            var first = legend.Document;
            Assert.NotNull(first);
            Assert.Equal(FontScaleState.Instance.Size("body") / 14, legend.DocumentScale);

            h.Send(IntentKind.ZoomFontRequested, 1);

            Assert.NotSame(first, legend.Document);
            Assert.Equal(FontScaleState.Instance.Size("body") / 14, legend.DocumentScale);
        }
        finally
        {
            // Presenter は FontScaleState（全体で 1 つ）を購読したまま残る。凡例を閉じておかないと、
            // 後のテストが倍率を変えたときに STA でないスレッドで描き直しが走る。
            h.Send(IntentKind.TabCloseRequested, legend);
        }
        Assert.Null(h.Open<LegendViewModel>());
    });

    // ---- DockPresenter ---------------------------------------------------------------

    private static (MediatorHarness H, DockLeaf Left, DockLeaf Right) TwoLeaves()
    {
        var h = new MediatorHarness(s => s.Settings.Layout = new DockNodeSettings
        {
            Axis = "Columns",
            First = new DockNodeSettings { Id = 1, Tabs = { nameof(SearchViewModel) } },
            Second = new DockNodeSettings { Id = 2, Tabs = { nameof(WordDetailViewModel) } },
        });
        _ = new DockPresenter(h.M, h.State.Layout);
        return (h, h.State.Layout.LeafById(1)!, h.State.Layout.LeafById(2)!);
    }

    [Fact]
    public void Dock_DropTargetIsShownAndCleared()
    {
        var (h, _, right) = TwoLeaves();
        var host = h.Shell;
        h.M.HitTester = _ => new HitLeaf(host, right.Id, new Size(600, 400), new Point(300, 200));

        h.Send(IntentKind.TabGripPressed, fill: c => { c.TabKind = nameof(SearchViewModel); c.LeafId = 1; c.LeafLocalPoint = new Point(0, 0); });
        h.Send(IntentKind.PointerMoved, fill: c => c.LeafLocalPoint = new Point(30, 0), screen: new Point(1, 1));
        Assert.True(right.IsDropTarget);

        h.Send(IntentKind.CancelRequested);
        Assert.False(right.IsDropTarget);
    }

    [Fact]
    public void Dock_SplitPreviewIsShownAndCleared()
    {
        var h = new MediatorHarness();
        _ = new DockPresenter(h.M, h.State.Layout);
        h.OpenDictionary(MediatorHarness.NewWord(1, "a"));
        h.Send(IntentKind.ShowExamplesRequested);
        var examples = h.Open<ExamplesViewModel>()!;
        var leaf = h.State.Layout.LeafOf(examples)!;
        var host = h.Shell;
        h.M.HitTester = _ => new HitLeaf(host, leaf.Id, new Size(600, 400), new Point(10, 200));

        h.Send(IntentKind.TabGripPressed, fill: c => { c.TabKind = examples.Kind; c.LeafId = leaf.Id; c.LeafLocalPoint = new Point(0, 0); });
        h.Send(IntentKind.PointerMoved, fill: c => c.LeafLocalPoint = new Point(30, 0), screen: new Point(1, 1));
        Assert.NotNull(leaf.Preview);

        h.Send(IntentKind.CancelRequested);
        Assert.Null(leaf.Preview);
    }

    [Fact]
    public void Dock_JoinTargetsAreShownAndCleared()
    {
        var (h, left, right) = TwoLeaves();
        var host = h.Shell;
        h.M.HitTester = _ => new HitLeaf(host, right.Id, new Size(600, 400), new Point(10, 10));

        h.Send(IntentKind.AreaGripPressed, fill: c => { c.LeafId = left.Id; c.Corner = AreaCorner.TopRight; c.LeafLocalPoint = new Point(600, 0); });
        h.Send(IntentKind.PointerMoved, fill: c => { c.LeafLocalPoint = new Point(700, 50); c.LeafSize = new Size(600, 400); }, screen: new Point(1, 1));
        Assert.True(right.IsJoinTarget);

        h.Send(IntentKind.CancelRequested);
        Assert.False(right.IsJoinTarget);
        Assert.False(left.IsJoinTarget);
    }

    [Fact]
    public void Dock_RowDragMovesItemsAndCancelRestores() => Sta.Run(() =>
    {
        var h = new MediatorHarness();
        _ = new DockPresenter(h.M, h.State.Layout);
        var rows = new ObservableCollection<string> { "a", "b", "c" };
        var list = new ItemsControl { ItemsSource = rows };

        h.Send(IntentKind.RowGripPressed, list, c => { c.RowIndex = 0; c.LeafLocalPoint = new Point(0, 0); });
        h.Send(IntentKind.PointerMoved, fill: c => { c.RowIndex = 2; c.LeafLocalPoint = new Point(0, 40); });
        Assert.Equal(new[] { "b", "c", "a" }, rows);

        h.Send(IntentKind.CancelRequested);
        Assert.Equal(new[] { "a", "b", "c" }, rows);
    });
}
