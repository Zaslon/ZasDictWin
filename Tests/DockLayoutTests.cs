using System.Windows;
using System.Windows.Input;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Tests;

public class DockLayoutTests
{
    private static (DockLayout Layout, AppSettings Settings, Func<int> Saves) New(DockNodeSettings? saved = null, params DockFloatSettings[] floats)
    {
        var settings = new AppSettings { Layout = saved, Floats = floats.ToList() };
        var saves = 0;
        return (new DockLayout(settings, () => saves++), settings, () => saves);
    }

    private static DockNodeSettings Leaf(int id, params string[] tabs) => new() { Id = id, Tabs = tabs.ToList() };

    private static DockNodeSettings Split(DockNodeSettings first, DockNodeSettings second, string axis = "Columns", double ratio = 0.5)
        => new() { Axis = axis, First = first, Second = second, Ratio = ratio };

    private static IEnumerable<DockNodeSettings> SavedLeaves(DockNodeSettings node)
        => node.First is { } a && node.Second is { } b ? SavedLeaves(a).Concat(SavedLeaves(b)) : new[] { node };

    // ---- 起動時の組み立て ------------------------------------------------------------

    [Fact]
    public void NoSettings_StartsWithSearchLeftAndDetailRight()
    {
        var (layout, _, _) = New();

        var root = Assert.IsType<DockSplit>(layout.Root);
        Assert.Equal((DockAxis.Columns, 0.31), (root.Axis, root.Ratio));
        Assert.Equal(new Dictionary<string, int> { [nameof(SearchViewModel)] = 1, [nameof(WordDetailViewModel)] = 2 }, layout.Homes);
    }

    [Fact]
    public void TooDeepSettings_FallBackToDefault()
    {
        var node = Leaf(1, "X");
        for (var i = 0; i < 12; i++) node = Split(node, Leaf(100 + i));

        var (layout, _, _) = New(node);

        // 深すぎる枝は読まず、読めた側だけで組む。根まで読めなければ既定の割り付け。
        Assert.True(layout.AllLeaves.Count() < 13);
        Assert.DoesNotContain("X", layout.Homes.Keys);
    }

    [Fact]
    public void LeavesWithoutId_AreRenumberedWithoutColliding()
    {
        var (layout, _, _) = New(Split(Leaf(0, "A"), Leaf(5, "B")));

        var ids = layout.AllLeaves.Select(l => l.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Contains(5, ids);
        // 後から作る枠も読み込んだ番号と重ならない。
        Assert.True(layout.Split(layout.LeafById(5)!, DockAxis.Rows, 0.5, true)!.Id > 5);
    }

    [Fact]
    public void Floats_AreRestoredWithUndecidedPositionAsNaN()
    {
        var (layout, _, _) = New(null, new DockFloatSettings { Width = 300, Height = 200, Node = Leaf(7, nameof(IpaToolViewModel)) },
            new DockFloatSettings { Node = null });

        var host = Assert.Single(layout.Floats);
        Assert.True(double.IsNaN(host.Bounds.X));
        Assert.Equal(300, host.Bounds.Width);
        Assert.Equal(7, layout.Homes[nameof(IpaToolViewModel)]);
    }

    // ---- 開く・閉じる ------------------------------------------------------------------

    [Fact]
    public void Add_UsesRememberedLeaf_ThenNewWindowForTools_ThenMain()
    {
        var (layout, _, _) = New(Split(Leaf(1, nameof(StatsViewModel)), Leaf(2)));
        var stats = new StatsViewModel(null);
        var ipa = new IpaToolViewModel();
        var legend = new LegendViewModel("", false);

        layout.Add(stats);
        layout.Add(ipa);

        Assert.Equal(1, layout.LeafOf(stats)!.Id);
        Assert.Same(layout.Floats.Single(), layout.FloatOf(ipa));
        Assert.Equal(new Size(480, 360), layout.Floats.Single().Bounds.Size);   // 窓の既定の大きさは種類ごと

        layout.Remove(stats);
        layout.Add(legend);   // 本体側の行き先はまだ知らない → 独立ウィンドウ
        Assert.NotNull(layout.FloatOf(legend));
    }

    [Fact]
    public void Main_IsTheLeafHoldingWordDetail()
    {
        var (layout, _, _) = New();
        var main = new MainViewModel(new AppSettings(), () => { });
        layout.Place(new WordDetailViewModel(main), layout.LeafById(2)!);
        Assert.Equal(2, layout.Main.Id);
    }

    [Fact]
    public void RemovingLastTab_FoldsTheEmptyLeafIntoItsSibling()
    {
        var (layout, _, _) = New(Split(Leaf(1), Leaf(2)));
        var stats = new StatsViewModel(null);
        layout.Place(stats, layout.LeafById(1)!);

        layout.Remove(stats);

        Assert.Equal(2, Assert.IsType<DockLeaf>(layout.Root).Id);
    }

    [Fact]
    public void Reorder_ClampsIntoRange()
    {
        var (layout, _, _) = New(Leaf(1));
        var leaf = layout.LeafById(1)!;
        var a = new StatsViewModel(null);
        var b = new IpaToolViewModel();
        layout.Place(a, leaf);
        layout.Place(b, leaf);

        layout.Reorder(a, 99);

        Assert.Equal(new OverlayViewModel[] { b, a }, leaf.Items);
    }

    // ---- 割る・結合・畳む ------------------------------------------------------------

    [Fact]
    public void Split_ReplacesLeafWithSplitWhereverItLives()
    {
        var (layout, _, _) = New(Leaf(1));
        var fresh = layout.Split(layout.LeafById(1)!, DockAxis.Rows, 0.3, newIsSecond: true)!;

        var root = Assert.IsType<DockSplit>(layout.Root);
        Assert.Same(fresh, root.Second);
        Assert.Equal(DockAxis.Rows, root.Axis);

        // 独立ウィンドウの根を割ると、その窓の根が差し替わる。
        var ipa = new IpaToolViewModel();
        var host = layout.Float(ipa, new Point(1, 2))!;
        layout.Split(layout.LeafOf(ipa)!, DockAxis.Columns, 0.5, newIsSecond: false);
        Assert.IsType<DockSplit>(host.Root);
        Assert.Null(host.Root.Parent);
    }

    [Fact]
    public void Split_AtLimit_IsRefused()
    {
        var (layout, _, _) = New(Leaf(1));
        while (layout.AllLeaves.Count() < 24) layout.Split(layout.AllLeaves.Last(), DockAxis.Columns, 0.5, true);
        Assert.Null(layout.Split(layout.AllLeaves.First(), DockAxis.Columns, 0.5, true));
    }

    [Fact]
    public void Join_MovesEveryTabOfTheSiblingIntoSurvivor()
    {
        var (layout, _, _) = New(Split(Leaf(1), Split(Leaf(2), Leaf(3), "Rows")));
        var a = new StatsViewModel(null);
        var b = new IpaToolViewModel();
        layout.Place(a, layout.LeafById(2)!);
        layout.Place(b, layout.LeafById(3)!);
        var survivor = layout.LeafById(1)!;

        layout.Join(survivor);

        Assert.Same(survivor, layout.Root);
        Assert.Equal(new OverlayViewModel[] { a, b }, survivor.Items);
        Assert.NotNull(survivor.Selected);
        Assert.Equal(1, layout.Homes[nameof(IpaToolViewModel)]);
    }

    [Fact]
    public void Dissolve_MovesTabsToSiblingOrClosesSingleLeafWindow()
    {
        var (layout, _, _) = New(Split(Leaf(1), Leaf(2)));
        var stats = new StatsViewModel(null);
        layout.Place(stats, layout.LeafById(2)!);

        layout.Dissolve(layout.LeafById(2)!);
        Assert.Same(layout.LeafById(1), layout.LeafOf(stats));

        var ipa = new IpaToolViewModel();
        var host = layout.Float(ipa, null)!;
        layout.Dissolve(layout.LeafOf(ipa)!);
        Assert.DoesNotContain(host, layout.Floats);
        Assert.Null(layout.FloatOf(ipa));       // 窓ごと畳むと中のタブは本体へ引き取る
        Assert.NotNull(layout.LeafOf(ipa));
    }

    [Fact]
    public void Dissolve_OnlyLeafOfMainWindow_IsKept()
    {
        var (layout, _, _) = New(Leaf(1));
        layout.Dissolve(layout.LeafById(1)!);
        Assert.Equal(1, Assert.IsType<DockLeaf>(layout.Root).Id);
    }

    [Fact]
    public void Float_SingleTabWindow_CannotBeFloatedAgain()
    {
        var (layout, _, _) = New();
        var ipa = new IpaToolViewModel();
        layout.Float(ipa, null);
        Assert.Null(layout.Float(ipa, new Point(5, 5)));
        Assert.Single(layout.Floats);
    }

    [Fact]
    public void FloatTitle_JoinsSelectedTabTitles()
    {
        var (layout, _, _) = New();
        var ipa = new IpaToolViewModel();
        var host = layout.Float(ipa, null)!;
        var other = layout.Split(layout.LeafOf(ipa)!, DockAxis.Columns, 0.5, true)!;
        var stats = new StatsViewModel(null);
        layout.Place(stats, other);

        Assert.Equal($"{ipa.Title} / {stats.Title}", host.Title);
    }

    [Fact]
    public void Move_ToSameLeaf_DoesNothing()
    {
        var (layout, _, _) = New(Split(Leaf(1), Leaf(2)));
        var stats = new StatsViewModel(null);
        var leaf = layout.LeafById(1)!;
        layout.Place(stats, leaf);
        layout.Move(stats, leaf);
        Assert.Same(leaf, layout.LeafOf(stats));
    }

    // ---- 書き戻し ------------------------------------------------------------------

    [Fact]
    public void Persist_WritesTreeOpenTabsAndRememberedKinds()
    {
        var (layout, settings, saves) = New(Split(Leaf(1, nameof(StatsViewModel)), Leaf(2), "Rows", 0.4));
        var ipa = new IpaToolViewModel();
        layout.Place(ipa, layout.LeafById(2)!);

        layout.Persist();

        Assert.Equal(1, saves());
        Assert.Equal(("Rows", 0.4), (settings.Layout!.Axis, settings.Layout.Ratio));
        var leaves = SavedLeaves(settings.Layout).ToList();
        Assert.Equal(new[] { nameof(StatsViewModel) }, leaves[0].Tabs);       // 閉じていても行き先は残す
        Assert.Equal(new[] { nameof(IpaToolViewModel) }, leaves[1].Tabs);
    }

    [Fact]
    public void Persist_DropsEmptyForgottenWindowsAndWritesUndecidedPositionAsNull()
    {
        var (layout, settings, _) = New();
        var ipa = new IpaToolViewModel();
        layout.Float(ipa, null);
        var stats = new StatsViewModel(null);
        var moved = layout.Float(stats, new Point(10, 20))!;
        layout.Move(stats, layout.LeafById(1)!);   // 行き先が本体へ移った窓は空のまま忘れてよい

        layout.Persist();

        var saved = Assert.Single(settings.Floats);
        Assert.Null(saved.Left);
        Assert.Equal(nameof(IpaToolViewModel), saved.Node!.Tabs.Single());
        Assert.DoesNotContain(moved, layout.Floats);
    }

    [Fact]
    public void Persist_KeepsEmptyWindowThatIsStillAHome()
    {
        var (layout, settings, _) = New();
        var ipa = new IpaToolViewModel();
        layout.Float(ipa, new Point(10, 20));
        layout.Remove(ipa);

        layout.Persist();

        // 中身は無いが、次に開いたとき同じ位置へ出すために覚えておく。
        var saved = Assert.Single(settings.Floats);
        Assert.Equal(10, saved.Left);
    }

    [Fact]
    public void SavedLayout_RoundTripsThroughSettings()
    {
        var (first, settings, _) = New(Split(Leaf(1, "A", "B"), Leaf(2, "C"), "Columns", 0.25));
        first.Persist();

        var (second, _, _) = New(settings.Layout);

        Assert.Equal(first.Homes, second.Homes);
        Assert.Equal(0.25, Assert.IsType<DockSplit>(second.Root).Ratio);
    }

    // ---- 境目 ----------------------------------------------------------------------

    [Fact]
    public void DockSplit_ExposesGridLengthsForItsAxis()
    {
        var columns = new DockSplit(DockAxis.Columns, new DockLeaf(1), new DockLeaf(2), 0.25);
        Assert.Equal((0.25, 0.75), (columns.Column0.Value, columns.Column2.Value));
        Assert.Equal((DockSplit.GripThickness, 0.0), (columns.Column1.Value, columns.Row1.Value));
        Assert.Equal((0, 1, 0, 2), (columns.GripRow, columns.GripColumn, columns.SecondRow, columns.SecondColumn));
        Assert.Same(Cursors.SizeWE, columns.GripCursor);

        var rows = new DockSplit(DockAxis.Rows, new DockLeaf(1), new DockLeaf(2), 0.99);
        Assert.Equal(0.95, rows.Ratio);   // 端に寄せすぎない
        Assert.Equal((1, 0, 2, 0), (rows.GripRow, rows.GripColumn, rows.SecondRow, rows.SecondColumn));
        Assert.Same(Cursors.SizeNS, rows.GripCursor);
    }

    [Fact]
    public void DockSplit_ResizeClampsAndIgnoresZeroTotal()
    {
        var (layout, _, _) = New(Split(Leaf(1), Leaf(2)));
        var split = Assert.IsType<DockSplit>(layout.Root);
        var raised = new List<string?>();
        split.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        layout.Resize(split, 100, 0);
        Assert.Equal(0.5, split.Ratio);

        layout.Resize(split, 10_000, 1000);
        Assert.Equal(1 - DockSplit.MinLeafSize / 1000, split.Ratio, 6);
        Assert.Contains(nameof(split.Column0), raised);
        Assert.Same(split, layout.SplitById(split.Id));
    }

    [Fact]
    public void SelectedChanged_ReportsNewlyShownTab()
    {
        var (layout, _, _) = New(Leaf(1));
        OverlayViewModel? shown = null;
        layout.SelectedChanged = vm => shown = vm;
        var stats = new StatsViewModel(null);

        layout.Place(stats, layout.LeafById(1)!);

        Assert.Same(stats, shown);
        Assert.True(stats.IsActive);
    }
}
