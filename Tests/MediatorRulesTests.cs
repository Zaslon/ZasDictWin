using System.Windows;
using ZasDictWin.Mediator;
using ZasDictWin.Root;
using ZasDictWin.ViewModels;
using E = ZasDictWin.Mediator.DragEffect;
using Esc = ZasDictWin.Mediator.EscRouter.EscTarget;

namespace ZasDictWin.Tests;

public class MediatorRulesTests
{
    // ---- DragEffect.Combine ------------------------------------------------------

    private static readonly E A = new E.ClearJoinTargets();
    private static readonly E B = new E.HideGhost();
    private static readonly E C = new E.Persist();

    [Fact]
    public void Combine_OneEffect_IsItself() => Assert.Same(A, E.Combine(A));

    [Fact]
    public void Combine_Empty_IsNone()
    {
        Assert.Same(E.None.Instance, E.Combine());
        Assert.Same(E.None.Instance, E.Combine((E?)null));
        Assert.Same(E.None.Instance, E.Combine(E.None.Instance));
    }

    [Fact]
    public void Combine_FlattensNested() => Assert.Equal(new E.Many(new[] { A, B, C }), E.Combine(new E.Many(new[] { A, B }), C));

    [Fact]
    public void Combine_SkipsNullAndNone() => Assert.Equal(new E.Many(new[] { A, B }), E.Combine(A, null, E.None.Instance, B));

    // ---- EscRouter ---------------------------------------------------------------

    [Theory]
    [InlineData(true, DragPhase.Idle, ShellPhase.Normal, HostRole.Shell, true, Esc.ClosePopupLayer)]
    [InlineData(false, DragPhase.TabOverLeaf, ShellPhase.Normal, HostRole.Shell, true, Esc.CancelDrag)]
    [InlineData(false, DragPhase.Idle, ShellPhase.Modal, HostRole.Shell, true, Esc.DismissModal)]
    [InlineData(false, DragPhase.Idle, ShellPhase.Normal, HostRole.Shell, true, Esc.CloseActiveTabInShell)]
    [InlineData(false, DragPhase.Idle, ShellPhase.Normal, HostRole.Floating, true, Esc.CloseActiveTabInHost)]
    [InlineData(false, DragPhase.Idle, ShellPhase.Normal, HostRole.Settings, false, Esc.CloseActiveTabInHost)]
    public void Esc_EachRow(bool popup, DragPhase drag, ShellPhase shell, HostRole role, bool closable, Esc expected)
        => Assert.Equal(expected, EscRouter.Resolve(popup, drag, shell, role, closable));

    [Fact]
    public void Esc_UpperRowWins()
    {
        Assert.Equal(Esc.ClosePopupLayer, EscRouter.Resolve(true, DragPhase.AreaSplitPreview, ShellPhase.Modal, HostRole.Shell, true));
        Assert.Equal(Esc.CancelDrag, EscRouter.Resolve(false, DragPhase.RowReordering, ShellPhase.Modal, HostRole.Shell, true));
    }

    [Theory]
    [InlineData(HostRole.Stream)]
    [InlineData(HostRole.Count)]
    public void Esc_StreamAndCount_DoNothing(HostRole role)
        => Assert.Equal(Esc.None, EscRouter.Resolve(false, DragPhase.Idle, ShellPhase.Normal, role, true));

    [Fact]
    public void Esc_ShellWithoutClosableTab_DoesNothing()
        => Assert.Equal(Esc.None, EscRouter.Resolve(false, DragPhase.Idle, ShellPhase.Normal, HostRole.Shell, false));

    // ---- CommandGate -------------------------------------------------------------

    private static GateContext Gate(
        ShellPhase shell = ShellPhase.Normal, DocPhase doc = DocPhase.Clean,
        bool gitHub = true, bool busy = false, bool synced = false, bool editor = false,
        bool selection = true, bool history = true, params string[] open)
        => new(shell, doc, gitHub, busy, synced, editor, new HashSet<string>(open), selection, history, history);

    public static IEnumerable<object[]> AllCommands => Enum.GetValues<AppCommand>().Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(AllCommands))]
    public void Gate_MinimalContext_Enables(AppCommand command) => Assert.True(CommandGate.CanExecute(command, Gate()));

    [Theory]
    [InlineData(AppCommand.Open, "editor")]
    [InlineData(AppCommand.NewDictionary, "editor")]
    [InlineData(AppCommand.Save, "nodoc")]
    [InlineData(AppCommand.SaveAs, "nodoc")]
    [InlineData(AppCommand.GitHubLoad, "local")]
    [InlineData(AppCommand.GitHubLoad, "editor")]
    [InlineData(AppCommand.GitHubCommit, "local")]
    [InlineData(AppCommand.GitHubCommit, "synced")]
    [InlineData(AppCommand.NewWord, "WordEditViewModel")]
    [InlineData(AppCommand.EditWord, "noselection")]
    [InlineData(AppCommand.DuplicateWord, "WordEditViewModel")]
    [InlineData(AppCommand.DeleteWord, "noselection")]
    [InlineData(AppCommand.ShowBrowser, "BrowserTabViewModel")]
    [InlineData(AppCommand.ShowExamples, "ExamplesViewModel")]
    [InlineData(AppCommand.EditExample, "ExampleEditViewModel")]
    [InlineData(AppCommand.Undo, "editor")]
    [InlineData(AppCommand.Undo, "nohistory")]
    [InlineData(AppCommand.Undo, "busy")]
    [InlineData(AppCommand.Redo, "editor")]
    [InlineData(AppCommand.Redo, "nohistory")]
    // ツール類と設定はモーダル中にしか無効にならない（Gate_ModalOrClosing_DisablesEverything で見る）。
    public void Gate_DisablingContext_Disables(AppCommand command, string why)
    {
        var ctx = why switch
        {
            "editor" => Gate(editor: true),
            "nodoc" => Gate(doc: DocPhase.NoDocument),
            "local" => Gate(gitHub: false),
            "synced" => Gate(synced: true),
            "noselection" => Gate(selection: false),
            "nohistory" => Gate(history: false),
            "busy" => Gate(busy: true),
            _ => Gate(open: new[] { why }),
        };
        Assert.False(CommandGate.CanExecute(command, ctx));
    }

    [Theory]
    [MemberData(nameof(AllCommands))]
    public void Gate_ModalOrClosing_DisablesEverything(AppCommand command)
    {
        Assert.False(CommandGate.CanExecute(command, Gate(shell: ShellPhase.Modal)));
        Assert.False(CommandGate.CanExecute(command, Gate(shell: ShellPhase.Closing)));
    }

    public static IEnumerable<object[]> BusyContexts => new[]
    {
        new object[] { Gate(shell: ShellPhase.ModalBusy) },
        new object[] { Gate(busy: true) },
    };

    [Theory]
    [MemberData(nameof(BusyContexts))]
    public void Gate_Busy_DisablesGitHubAndDictionaryOnly(GateContext busy)
    {
        foreach (var c in new[] { AppCommand.GitHubLoad, AppCommand.GitHubCommit, AppCommand.Open, AppCommand.NewDictionary, AppCommand.Save, AppCommand.SaveAs })
            Assert.False(CommandGate.CanExecute(c, busy));
        Assert.True(CommandGate.CanExecute(AppCommand.NewWord, busy));
        Assert.True(CommandGate.CanExecute(AppCommand.ShowStats, busy));
    }

    [Fact]
    public void Gate_UnrelatedCondition_DoesNotDisable()
    {
        // 新しい辞書は開いていなくても作れる。保存は編集中でも止めない（差し替えではないため）。
        Assert.True(CommandGate.CanExecute(AppCommand.NewDictionary, Gate(doc: DocPhase.NoDocument)));
        Assert.True(CommandGate.CanExecute(AppCommand.Save, Gate(editor: true)));
    }

    // ---- LayoutRules -------------------------------------------------------------

    [Fact]
    public void Placement_Remembered_Floating_Main()
    {
        var homes = new Dictionary<string, int> { ["A"] = 3 };
        var leaves = new HashSet<int> { 1, 3 };
        Assert.Equal(LayoutRules.PlaceTarget.RememberedLeaf, LayoutRules.ResolvePlacement("A", true, homes, leaves, out var id));
        Assert.Equal(3, id);
        Assert.Equal(LayoutRules.PlaceTarget.NewFloat, LayoutRules.ResolvePlacement("B", true, homes, leaves, out _));
        Assert.Equal(LayoutRules.PlaceTarget.MainLeaf, LayoutRules.ResolvePlacement("B", false, homes, leaves, out _));
    }

    [Fact]
    public void Placement_RememberedLeafGone_FallsBack()
    {
        var homes = new Dictionary<string, int> { ["A"] = 9 };
        var leaves = new HashSet<int> { 1 };
        Assert.Equal(LayoutRules.PlaceTarget.MainLeaf, LayoutRules.ResolvePlacement("A", false, homes, leaves, out var id));
        Assert.Equal(-1, id);
        Assert.Equal(LayoutRules.PlaceTarget.NewFloat, LayoutRules.ResolvePlacement("A", true, homes, leaves, out _));
    }

    [Fact]
    public void ClampRatio_Boundaries()
    {
        const double min = DockSplit.MinLeafSize;
        // 割り付けが最小幅 2 つぶん以下なら動かさない（ちょうど 2 つぶんも含む）。
        Assert.Equal(0.5, LayoutRules.ClampRatio(0.5, 0.01, min * 2, min));
        Assert.Equal(0.5, LayoutRules.ClampRatio(0.5, 0.01, min * 2 - 1, min));
        Assert.Equal(min / (min * 2 + 1), LayoutRules.ClampRatio(0.5, 0.0, min * 2 + 1, min), 6);
        var total = 1000.0;
        Assert.Equal(min / total, LayoutRules.ClampRatio(0.5, 0.0, total, min), 6);
        Assert.Equal(1 - min / total, LayoutRules.ClampRatio(0.5, 1.0, total, min), 6);
        Assert.Equal(0.5, LayoutRules.ClampRatio(0.5, 0.5004, total, min));           // 差が 0.0005 未満なら動かさない
        Assert.Equal(0.5006, LayoutRules.ClampRatio(0.5, 0.5006, total, min), 6);
    }

    [Fact]
    public void CanSplit_Boundaries()
    {
        const double min = DockSplit.MinLeafSize;
        var size = new Size(600, 400);
        Assert.False(LayoutRules.CanSplit(LayoutRules.MaxLeaves, size, DockAxis.Columns, min));
        Assert.True(LayoutRules.CanSplit(LayoutRules.MaxLeaves - 1, size, DockAxis.Columns, min));
        Assert.True(LayoutRules.CanSplit(1, new Size(min * 2, 400), DockAxis.Columns, min));
        Assert.False(LayoutRules.CanSplit(1, new Size(min * 2 - 1, 400), DockAxis.Columns, min));
        Assert.False(LayoutRules.CanSplit(1, new Size(600, min * 2 - 1), DockAxis.Rows, min));
    }

    [Fact]
    public void CanFloat_Boundaries()
    {
        Assert.False(LayoutRules.CanFloat(2, 1, true));
        Assert.True(LayoutRules.CanFloat(2, 2, true));
        Assert.True(LayoutRules.CanFloat(2, 1, false));
        Assert.False(LayoutRules.CanFloat(LayoutRules.MaxLeaves, 2, false));
    }

    [Fact]
    public void DissolveClosesHost_OnlyForFloatRootWithoutParent()
    {
        Assert.True(LayoutRules.DissolveClosesHost(false, true));
        Assert.False(LayoutRules.DissolveClosesHost(true, true));
        Assert.False(LayoutRules.DissolveClosesHost(false, false));
    }

    // ---- BrowserAddress ----------------------------------------------------------

    [Theory]
    [InlineData("", "https://www.google.com/")]
    [InlineData("   ", "https://www.google.com/")]
    [InlineData("http://example.com", "http://example.com")]
    [InlineData("https://example.com/x", "https://example.com/x")]
    [InlineData("file://C:/a.html", "file://C:/a.html")]
    [InlineData("zasdict", "https://www.google.com/search?q=zasdict")]
    [InlineData("動詞 変換", "https://www.google.com/search?q=%E5%8B%95%E8%A9%9E%20%E5%A4%89%E6%8F%9B")]
    [InlineData("dict.example.com", "https://dict.example.com")]
    public void NormalizeInput(string raw, string expected) => Assert.Equal(expected, BrowserAddress.Normalize(raw));

    // ---- ScreenRegistry ----------------------------------------------------------

    [Fact]
    public void Registry_CoversExactlyAllScreens()
    {
        // 登録漏れは例外にならず既定の規則で黙って動くので、画面の型と突き合わせて見つける。
        var screens = typeof(OverlayViewModel).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(OverlayViewModel)) && !t.IsAbstract)
            .Select(t => t.Name)
            .ToHashSet();
        Assert.Equal(screens, ScreenRegistry.All.Select(r => r.Kind).ToHashSet());
    }

    [Theory]
    [InlineData("SomethingNew")]
    [InlineData("")]
    [InlineData(null)]
    public void Registry_UnknownKind_ReturnsDefault(string? kind)
    {
        var rule = ScreenRegistry.For(kind);
        Assert.False(rule.PrefersFloating || rule.IsPinned || rule.BlocksDictionarySwap);
    }
}
