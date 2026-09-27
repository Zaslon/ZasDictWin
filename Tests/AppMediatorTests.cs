using System.Windows;
using ZasDictWin.Mediator;
using ZasDictWin.Models;
using ZasDictWin.Root;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Tests;

public class AppMediatorTests
{
    private static WordEditViewModel OpenNewWordEditor(MediatorHarness h, string form)
    {
        h.Send(IntentKind.NewWordRequested);
        var editor = h.Open<WordEditViewModel>()!;
        editor.Form = form;
        return editor;
    }

    // ---- DocPhase ---------------------------------------------------------------

    [Fact]
    public void Open_Commit_Save_MovesDocPhase()
    {
        var h = new MediatorHarness();
        Assert.Equal(DocPhase.NoDocument, h.M.Doc);

        h.OpenDictionary(MediatorHarness.NewWord(1, "a"));
        Assert.Equal(DocPhase.Clean, h.M.Doc);

        OpenNewWordEditor(h, "b");
        h.Send(IntentKind.WordCommitted);
        Assert.Equal(DocPhase.Dirty, h.M.Doc);
        Assert.Null(h.Open<WordEditViewModel>());

        h.Send(IntentKind.SaveRequested);
        Assert.Equal(DocPhase.Clean, h.M.Doc);
        Assert.Equal(new[] { "d.json" }, h.Services.SavedPaths);
    }

    [Fact]
    public void OpenFailure_ShowsConfirmAndKeepsPhase()
    {
        var h = new MediatorHarness();
        h.Services.NextOpenPath = "missing.json";
        h.Send(IntentKind.OpenDictionaryRequested);

        Assert.Equal(DocPhase.NoDocument, h.M.Doc);
        Assert.Equal(ShellPhase.Modal, h.M.Shell);
        Assert.Single(h.Confirm!.Choices);
    }

    [Fact]
    public void SaveFailure_StaysDirty_AndKeepsPendingChanges()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(MediatorHarness.NewWord(1, "a"));
        OpenNewWordEditor(h, "b");
        h.Send(IntentKind.WordCommitted);
        h.Services.SaveFails = true;

        h.Send(IntentKind.SaveRequested);

        Assert.Equal(DocPhase.Dirty, h.M.Doc);
        Assert.Single(h.M.PendingChanges);
        Assert.Equal(ShellPhase.Modal, h.M.Shell);
    }

    [Fact]
    public void AutoSave_SavesExactlyOncePerCommit()
    {
        var h = new MediatorHarness(s => s.Settings.AutoSave = true);
        h.OpenDictionary(MediatorHarness.NewWord(1, "a"));
        OpenNewWordEditor(h, "b");

        h.Send(IntentKind.WordCommitted);

        Assert.Single(h.Services.SavedPaths);
        Assert.Equal(DocPhase.Clean, h.M.Doc);
        // 保存が成功したときだけ更新履歴を書く。
        Assert.Single(h.Services.Appended);
    }

    [Fact]
    public void NoDocument_WordOperationsDoNothing()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.NewWordRequested);
        h.Send(IntentKind.DeleteWordRequested);
        h.Send(IntentKind.WordCommitted);
        Assert.Null(h.Open<WordEditViewModel>());
        Assert.Equal(ShellPhase.Normal, h.M.Shell);
    }

    // ---- 更新履歴の集約 ---------------------------------------------------------------

    [Fact]
    public void Changelog_AddThenChange_IsOneRow()
    {
        var h = new MediatorHarness();
        h.OpenDictionary();
        OpenNewWordEditor(h, "a");
        h.Send(IntentKind.WordCommitted);
        var added = h.State.SelectedWord!;
        h.Send(IntentKind.EditWordRequested, added);
        h.Open<WordEditViewModel>()!.TagsText = "x";
        h.Send(IntentKind.WordCommitted);

        Assert.Equal(new[] { "ADD" }, h.M.PendingChanges.Select(c => c.Operation));
    }

    [Fact]
    public void Changelog_ChangeThenChange_IsOneRow()
    {
        var h = new MediatorHarness();
        var b = MediatorHarness.NewWord(1, "b");
        h.OpenDictionary(b);
        for (var i = 0; i < 2; i++)
        {
            h.Send(IntentKind.EditWordRequested, b);
            h.Open<WordEditViewModel>()!.TagsText = $"t{i}";
            h.Send(IntentKind.WordCommitted);
        }
        Assert.Equal(new[] { "CHANGE" }, h.M.PendingChanges.Select(c => c.Operation));
    }

    [Fact]
    public void Changelog_DeleteThenChangeOfSameForm_IsTwoRows()
    {
        var h = new MediatorHarness();
        var c1 = MediatorHarness.NewWord(1, "c", "one");
        var c2 = MediatorHarness.NewWord(2, "c", "two");
        h.OpenDictionary(c1, c2);

        h.Send(IntentKind.DeleteWordRequested, c1);
        h.Send(IntentKind.ConfirmChoiceSelected, 0);
        h.Send(IntentKind.EditWordRequested, c2);
        h.Open<WordEditViewModel>()!.TagsText = "x";
        h.Send(IntentKind.WordCommitted);

        Assert.Equal(new[] { "DELETE", "CHANGE" }, h.M.PendingChanges.Select(c => c.Operation));
    }

    [Fact]
    public void Changelog_ChangesOfDifferentForms_AreTwoRows()
    {
        var h = new MediatorHarness();
        var x = MediatorHarness.NewWord(1, "x");
        var y = MediatorHarness.NewWord(2, "y");
        h.OpenDictionary(x, y);
        foreach (var w in new[] { x, y })
        {
            h.Send(IntentKind.EditWordRequested, w);
            h.Open<WordEditViewModel>()!.TagsText = "t";
            h.Send(IntentKind.WordCommitted);
        }
        Assert.Equal(2, h.M.PendingChanges.Count);
    }

    // ---- 単語エディタの差し替え ---------------------------------------------------------

    [Fact]
    public void RequestEditWord_SameWord_OnlyBringsToFront()
    {
        var h = new MediatorHarness();
        var a = MediatorHarness.NewWord(1, "a");
        h.OpenDictionary(a);
        h.Send(IntentKind.EditWordRequested, a);
        var editor = h.Open<WordEditViewModel>();

        h.Send(IntentKind.EditWordRequested, a);

        Assert.Same(editor, h.Open<WordEditViewModel>());
        Assert.Single(h.State.Layout.Overlays.OfType<WordEditViewModel>());
    }

    [Fact]
    public void RequestEditWord_UnchangedEditor_IsReplaced()
    {
        var h = new MediatorHarness();
        var a = MediatorHarness.NewWord(1, "a");
        var b = MediatorHarness.NewWord(2, "b");
        h.OpenDictionary(a, b);
        h.Send(IntentKind.EditWordRequested, a);

        h.Send(IntentKind.EditWordRequested, b);

        Assert.Same(b, h.Open<WordEditViewModel>()!.Source);
        Assert.Equal(ShellPhase.Normal, h.M.Shell);
    }

    [Fact]
    public void RequestEditWord_ChangedEditor_AsksFirst()
    {
        var h = new MediatorHarness();
        var a = MediatorHarness.NewWord(1, "a");
        var b = MediatorHarness.NewWord(2, "b");
        h.OpenDictionary(a, b);
        h.Send(IntentKind.EditWordRequested, a);
        h.Open<WordEditViewModel>()!.TagsText = "changed";

        h.Send(IntentKind.EditWordRequested, b);
        Assert.Equal(ShellPhase.Modal, h.M.Shell);
        Assert.Same(a, h.Open<WordEditViewModel>()!.Source);

        h.Send(IntentKind.ConfirmChoiceSelected, 0);
        Assert.Same(b, h.Open<WordEditViewModel>()!.Source);
        Assert.Equal(ShellPhase.Normal, h.M.Shell);
    }

    // ---- ShellPhase ----------------------------------------------------------------

    [Fact]
    public void Modal_BlocksOpeningScreens()
    {
        var h = new MediatorHarness();
        var a = MediatorHarness.NewWord(1, "a");
        h.OpenDictionary(a);
        h.State.SelectedWord = a;
        h.Send(IntentKind.DeleteWordRequested);
        Assert.Equal(ShellPhase.Modal, h.M.Shell);

        h.Send(IntentKind.NewWordRequested);
        Assert.Null(h.Open<WordEditViewModel>());

        h.Send(IntentKind.ModalDismissRequested);
        Assert.Equal(ShellPhase.Normal, h.M.Shell);
        Assert.Contains(a, h.State.AllWords);
    }

    [Fact]
    public void Esc_DismissesModalWithoutRunningChoice()
    {
        var h = new MediatorHarness();
        var a = MediatorHarness.NewWord(1, "a");
        h.OpenDictionary(a);
        h.Send(IntentKind.DeleteWordRequested, a);

        h.Send(IntentKind.CancelRequested);

        Assert.Equal(ShellPhase.Normal, h.M.Shell);
        Assert.Contains(a, h.State.AllWords);
    }

    [Fact]
    public void Closing_IgnoresLaterIntents()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.WindowCloseRequested, new WindowBounds(1000, 700, false));
        Assert.Equal(ShellPhase.Closing, h.M.Shell);
        Assert.Equal(1000, h.Services.Settings.WindowWidth);
        Assert.Contains(new HostCommand.CloseHost(h.Shell.HostId), h.HostCommands);

        h.HostCommands.Clear();
        h.Send(IntentKind.StreamWindowToggleRequested);
        h.Send(IntentKind.SettingsRequested);
        Assert.Empty(h.HostCommands);
    }

    [Fact]
    public void Close_WhenDirty_AsksThreeWays()
    {
        var h = new MediatorHarness();
        h.OpenDictionary();
        OpenNewWordEditor(h, "a");
        h.Send(IntentKind.WordCommitted);

        h.Send(IntentKind.WindowCloseRequested, new WindowBounds(1000, 700, false));
        Assert.Equal(ShellPhase.Modal, h.M.Shell);
        Assert.Equal(3, h.Confirm!.Choices.Count);

        // 「編集を続ける」
        h.Send(IntentKind.ConfirmChoiceSelected, 2);
        Assert.Equal(ShellPhase.Normal, h.M.Shell);

        // 「保存して終了」
        h.Send(IntentKind.WindowCloseRequested, new WindowBounds(1000, 700, false));
        h.Send(IntentKind.ConfirmChoiceSelected, 0);
        Assert.Equal(ShellPhase.Closing, h.M.Shell);
        Assert.Single(h.Services.SavedPaths);
    }

    [Fact]
    public void Close_SaveAndExit_WhenSaveFails_KeepsRunning()
    {
        var h = new MediatorHarness();
        h.OpenDictionary();
        OpenNewWordEditor(h, "a");
        h.Send(IntentKind.WordCommitted);
        h.Services.SaveFails = true;

        h.Send(IntentKind.WindowCloseRequested, new WindowBounds(1000, 700, false));
        h.Send(IntentKind.ConfirmChoiceSelected, 0);

        Assert.NotEqual(ShellPhase.Closing, h.M.Shell);
        Assert.Equal(DocPhase.Dirty, h.M.Doc);
        Assert.DoesNotContain(new HostCommand.CloseHost(h.Shell.HostId), h.HostCommands);
    }

    [Fact]
    public void ChangedFiresOncePerIntent()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(MediatorHarness.NewWord(1, "a"));
        foreach (var kind in new[] { IntentKind.NewWordRequested, IntentKind.WordCommitted, IntentKind.SaveRequested, IntentKind.ZoomFontRequested })
        {
            var before = h.ChangedCount;
            h.Send(kind, kind == IntentKind.ZoomFontRequested ? 1 : null);
            Assert.Equal(before + 1, h.ChangedCount);
        }
    }

    [Fact]
    public void ZoomFont_ClampsAndSaves()
    {
        var h = new MediatorHarness(s => s.Settings.FontScale = 2.95);
        h.Send(IntentKind.ZoomFontRequested, 1);
        Assert.Equal(3.0, h.Services.Settings.FontScale);
        var saves = h.Services.SettingsSaves;
        h.Send(IntentKind.ZoomFontRequested, 1);
        Assert.Equal(3.0, h.Services.Settings.FontScale);
        Assert.Equal(saves, h.Services.SettingsSaves);
    }

    // ---- 一覧（DropDown / MenuButton）と Esc -------------------------------------------

    [Fact]
    public void Popup_EscClosesPopupFirst_ThenNextLayer()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.PopupLayerOpened);
        Assert.Contains(new HostCommand.SetPopupLayerOpen(true), h.HostCommands);

        h.HostCommands.Clear();
        h.Send(IntentKind.CancelRequested);
        Assert.Equal(new HostCommand[] { new HostCommand.ClosePopupLayer() }, h.HostCommands);

        h.Send(IntentKind.PopupLayerClosed);
        Assert.Contains(new HostCommand.SetPopupLayerOpen(false), h.HostCommands);
    }

    [Fact]
    public void Popup_OpeningSecond_ClosesFirst()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.PopupLayerOpened);
        h.HostCommands.Clear();
        h.Send(IntentKind.PopupLayerOpened);
        Assert.Equal(new HostCommand[] { new HostCommand.ClosePopupLayer() }, h.HostCommands);
    }

    [Fact]
    public void Esc_InShell_ClosesLastTouchedTab_NotPinned()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(MediatorHarness.NewWord(1, "a"));
        h.Send(IntentKind.ShowExamplesRequested);
        Assert.NotNull(h.Open<ExamplesViewModel>());

        h.Send(IntentKind.CancelRequested);
        Assert.Null(h.Open<ExamplesViewModel>());

        h.Send(IntentKind.CancelRequested);
        Assert.NotNull(h.Open<WordDetailViewModel>());
        Assert.NotNull(h.Open<SearchViewModel>());
    }

    // ---- ドラッグ ----------------------------------------------------------------------

    [Fact]
    public void TabDroppedOnSourceEdge_SplitsMovesAndPersistsOnce()
    {
        var h = new MediatorHarness();
        h.OpenDictionary(MediatorHarness.NewWord(1, "a"));
        h.Send(IntentKind.ShowExamplesRequested);
        var examples = h.Open<ExamplesViewModel>()!;
        var source = h.State.Layout.LeafOf(examples)!;
        Assert.Equal(2, source.Items.Count);
        var host = h.Shell;
        h.M.HitTester = _ => new HitLeaf(host, source.Id, new Size(600, 400), new Point(10, 200));

        h.Send(IntentKind.TabGripPressed, fill: c => { c.TabKind = examples.Kind; c.LeafId = source.Id; c.LeafLocalPoint = new Point(0, 0); });
        h.Send(IntentKind.PointerMoved, fill: c => { c.LeafId = source.Id; c.LeafLocalPoint = new Point(30, 0); }, screen: new Point(10, 200));
        Assert.Equal(DragPhase.TabOverSourceEdge, h.M.Drag);
        Assert.Contains(h.Effects, e => e is DragEffect.ShowSplitPreview);

        var before = h.Persists;
        h.Send(IntentKind.PointerReleased, screen: new Point(10, 200));

        Assert.Equal(before + 1, h.Persists);
        Assert.Equal(DragPhase.Idle, h.M.Drag);
        Assert.Equal(3, h.State.Layout.AllLeaves.Count());
        Assert.NotSame(source, h.State.Layout.LeafOf(examples));
    }

    [Fact]
    public void TabDroppedOutside_OpensFloatingWindowAfterShellIsReady()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.WindowActivated);
        h.OpenDictionary(MediatorHarness.NewWord(1, "a"));
        h.Send(IntentKind.ShowExamplesRequested);
        var examples = h.Open<ExamplesViewModel>()!;
        var source = h.State.Layout.LeafOf(examples)!;
        h.M.HitTester = _ => null;

        h.Send(IntentKind.TabGripPressed, fill: c => { c.TabKind = examples.Kind; c.LeafId = source.Id; });
        h.Send(IntentKind.PointerMoved, new Point(50, 60), c => c.LeafLocalPoint = new Point(40, 40), new Point(99, 99));
        Assert.Contains(h.HostCommands, c => c is HostCommand.ShowDragGhost);
        h.Send(IntentKind.PointerReleased);

        var floating = Assert.Single(h.State.Layout.Floats);
        Assert.Contains(new HostCommand.OpenFloating(floating), h.HostCommands);
        Assert.Contains(h.HostCommands, c => c is HostCommand.HideDragGhost);
    }

    [Fact]
    public void FloatingWindowClosedByUser_ClosesItsTabsAndWindow()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.WindowActivated);
        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowIpaTool);
        var floating = Assert.Single(h.State.Layout.Floats);
        var host = h.AddHost(HostRole.Floating, floating.Id);

        h.Send(IntentKind.WindowCloseRequested, from: host);

        Assert.Null(h.Open<IpaToolViewModel>());
        Assert.Contains(new HostCommand.CloseHost(floating.Id), h.HostCommands);
    }

    [Fact]
    public void ToolReopened_FocusesItsWindowInsteadOfRecreating()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.WindowActivated);
        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowIpaTool);
        var tool = h.Open<IpaToolViewModel>();
        var floating = Assert.Single(h.State.Layout.Floats);

        h.HostCommands.Clear();
        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowIpaTool);

        Assert.Same(tool, h.Open<IpaToolViewModel>());
        Assert.Contains(new HostCommand.FocusHost(floating.Id), h.HostCommands);
        Assert.DoesNotContain(h.HostCommands, c => c is HostCommand.OpenFloating);
    }

    // ---- メニュー ----------------------------------------------------------------------

    [Fact]
    public void Menus_MatchItemsOrderAndState()
    {
        var h = new MediatorHarness();
        var file = h.M.BuildFileMenu();
        Assert.Equal(new[] { IntentKind.OpenDictionaryRequested, IntentKind.NewDictionaryRequested, IntentKind.SaveRequested, IntentKind.SaveAsRequested },
            file.Select(i => i.Intent));
        Assert.Equal(new[] { false, false, true, false }, file.Select(i => i.IsPrimary));
        Assert.Equal(new[] { true, true, false, false }, file.Select(i => i.IsEnabled));

        var tools = h.M.BuildToolsMenu();
        Assert.Equal(new object?[] { AppCommand.ShowDialectTool, AppCommand.ShowIpaTool, AppCommand.ShowStats, AppCommand.ShowLegend, AppCommand.ShowChangelog },
            tools.Select(i => i.Payload));
        Assert.All(tools, i => Assert.True(i.IsEnabled));

        var window = h.M.BuildWindowMenu();
        Assert.Equal(2, window.Count);
        var closedHeader = window[0].Header;
        h.Send(IntentKind.StreamWindowToggleRequested);
        Assert.NotEqual(closedHeader, h.M.BuildWindowMenu()[0].Header);
    }

    [Fact]
    public void RowMenu_TargetsTheRowWord()
    {
        var h = new MediatorHarness();
        var a = MediatorHarness.NewWord(1, "a");
        h.OpenDictionary(a);
        var row = h.M.BuildRowMenu(a);
        Assert.Equal(new[] { IntentKind.EditWordRequested, IntentKind.DuplicateWordRequested, IntentKind.DeleteWordRequested }, row.Select(i => i.Intent));
        Assert.All(row, i => Assert.Same(a, i.Payload));
        Assert.All(row, i => Assert.True(i.IsEnabled));
        Assert.Empty(h.M.BuildRowMenu(null));

        OpenNewWordEditor(h, "b");
        Assert.All(h.M.BuildRowMenu(a), i => Assert.False(i.IsEnabled));
    }

    // ---- 凡例 --------------------------------------------------------------------------

    [Fact]
    public void LegendEdit_WritesMarkdownBackAndMarksDirty()
    {
        var h = new MediatorHarness();
        var doc = h.OpenDictionary(MediatorHarness.NewWord(1, "a"));
        doc.Root["legend"] = "# old";
        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowLegend);
        var legend = h.Open<LegendViewModel>()!;

        h.Send(IntentKind.LegendEditRequested);
        Assert.True(legend.IsEditing);
        Assert.Equal("# old", legend.Draft);
        // 書きかけの凡例を別の辞書へ書き込まないよう、編集中は差し替えを止める。
        Assert.False(h.M.CanExecute(AppCommand.Open));

        legend.Draft = "# new";
        h.Send(IntentKind.LegendCommitted);

        Assert.False(legend.IsEditing);
        Assert.Equal("# new", doc.Root["legend"]!.GetValue<string>());
        Assert.Equal("# new", legend.LegendMarkdown);
        Assert.Equal(DocPhase.Dirty, h.M.Doc);
        Assert.True(h.M.CanExecute(AppCommand.Open));
    }

    [Fact]
    public void LegendEdit_UnchangedOrCancelled_KeepsDocClean()
    {
        var h = new MediatorHarness();
        var doc = h.OpenDictionary(MediatorHarness.NewWord(1, "a"));
        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowLegend);
        var legend = h.Open<LegendViewModel>()!;

        h.Send(IntentKind.LegendEditRequested);
        // legend が無い辞書では、表示用の zpdicOnline を編集欄に持ち込まない。
        Assert.Equal("", legend.Draft);
        h.Send(IntentKind.LegendCommitted);
        Assert.False(legend.IsEditing);

        h.Send(IntentKind.LegendEditRequested);
        legend.Draft = "draft";
        h.Send(IntentKind.LegendEditCancelled);

        Assert.False(legend.IsEditing);
        Assert.Null(doc.Legend);
        Assert.Equal(DocPhase.Clean, h.M.Doc);
    }

    [Fact]
    public void LegendEdit_StructuredLegend_StaysJson()
    {
        var h = new MediatorHarness();
        var doc = h.OpenDictionary(MediatorHarness.NewWord(1, "a"));
        doc.Root["legend"] = new System.Text.Json.Nodes.JsonObject { ["k"] = 1 };
        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowLegend);
        var legend = h.Open<LegendViewModel>()!;
        h.Send(IntentKind.LegendEditRequested);

        legend.Draft = "{ broken";
        h.Send(IntentKind.LegendCommitted);
        Assert.True(legend.IsEditing);
        Assert.NotNull(legend.ValidationMessage);
        Assert.Equal(DocPhase.Clean, h.M.Doc);

        legend.Draft = "{ \"k\": 2 }";
        h.Send(IntentKind.LegendCommitted);
        Assert.False(legend.IsEditing);
        Assert.Equal(2, doc.Legend!["k"]!.GetValue<int>());
    }

    [Fact]
    public void LegendEdit_NoDocument_CannotEdit()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowLegend);
        var legend = h.Open<LegendViewModel>()!;

        h.Send(IntentKind.LegendEditRequested);

        Assert.False(legend.CanEdit);
        Assert.False(legend.IsEditing);

        h.OpenDictionary(MediatorHarness.NewWord(1, "a"));
        Assert.True(legend.CanEdit);
    }

    // ---- 設定の適用 ----------------------------------------------------------------------

    [Fact]
    public void SettingsApply_ClampsAndSkipsEmptyRelations()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.SettingsRequested);
        var form = Assert.IsType<HostCommand.OpenSettings>(h.HostCommands.Last()).State;
        form.FontScale = 5;
        form.StreamFontScale = 0.5;
        form.WindowWidth = 100;
        form.WindowHeight = 100;
        form.ReciprocalText = "";
        var settingsHost = h.AddHost(HostRole.Settings);

        h.Send(IntentKind.SettingsApplyRequested, from: settingsHost);

        var s = h.Services.Settings;
        Assert.Equal(3.0, s.FontScale);
        Assert.Equal(1.0, s.StreamFontScale);
        Assert.Equal(900, s.WindowWidth);
        Assert.Equal(600, s.WindowHeight);
        Assert.Null(h.Services.SavedRelations);
        Assert.Contains(new HostCommand.CloseHost(settingsHost.HostId), h.HostCommands);
    }

    [Fact]
    public void SettingsApply_LowerBounds()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.SettingsRequested);
        var form = Assert.IsType<HostCommand.OpenSettings>(h.HostCommands.Last()).State;
        form.FontScale = 0.1;
        form.StreamFontScale = 9;
        form.ReciprocalText = "a=b";
        h.Send(IntentKind.SettingsApplyRequested, from: h.AddHost(HostRole.Settings));

        Assert.Equal(0.6, h.Services.Settings.FontScale);
        Assert.Equal(6.0, h.Services.Settings.StreamFontScale);
        Assert.Equal("b", h.Services.SavedRelations!["a"]);
    }

    [Fact]
    public void Settings_OpensOnlyOnce()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.SettingsRequested);
        h.Send(IntentKind.SettingsRequested);
        Assert.Single(h.HostCommands.OfType<HostCommand.OpenSettings>());
    }

    // ---- 絞り込み ----------------------------------------------------------------------

    public static IEnumerable<object[]> FilterCombos =>
        from m in Enum.GetValues<SearchMode>()
        from s in Enum.GetValues<SearchScope>()
        select new object[] { m, s };

    [Theory]
    [MemberData(nameof(FilterCombos))]
    public void Filter_UsesModeAndScope(SearchMode mode, SearchScope scope)
    {
        var h = new MediatorHarness();
        var words = new[]
        {
            MediatorHarness.NewWord(1, "abc", "dog"), MediatorHarness.NewWord(2, "xab", "cab"),
            MediatorHarness.NewWord(3, "ab", "ab"), MediatorHarness.NewWord(4, "zzz", "ab cd"),
        };
        h.OpenDictionary(words);
        h.State.Query = "ab";
        h.Send(IntentKind.QueryChanged);
        h.Send(IntentKind.SearchModeChanged, mode.ToString());
        h.Send(IntentKind.SearchScopeChanged, scope.ToString());

        var expected = new SearchService(new TextProcessor(TextProcessor.DefaultSortOrder, ""), null)
            .Filter(h.State.AllWords, "ab", mode, scope).ToList();
        Assert.Equal(expected, h.State.FilteredWords);
        Assert.Equal(mode, h.State.SearchMode);
        Assert.Equal(scope, h.State.SearchScope);
    }

    [Fact]
    public void QueryChanged_WithSameQuery_KeepsSelection()
    {
        var h = new MediatorHarness();
        var a = MediatorHarness.NewWord(1, "a");
        h.OpenDictionary(a);
        h.State.SelectedWord = a;
        var before = h.State.FilteredWords.ToList();

        h.Send(IntentKind.QueryChanged);

        Assert.Same(a, h.State.SelectedWord);
        Assert.Equal(before, h.State.FilteredWords);
    }

    // ---- GitHub ------------------------------------------------------------------------

    private static MediatorHarness GitHubHarness(Action<StubServices>? setup = null)
        => new(s =>
        {
            s.Settings.Mode = EditMode.GitHub;
            setup?.Invoke(s);
        });

    [Fact]
    public void GitHubLoad_WithDiff_Confirms_ThenLoadsAndSyncs()
    {
        var h = GitHubHarness(s => s.GetFile = _ => new GitHubFileResult(true, "", "{\"new\":1}"));

        h.Send(IntentKind.GitHubLoadRequested);
        Assert.Equal(ShellPhase.Modal, h.M.Shell);

        h.Send(IntentKind.ConfirmChoiceSelected, 0);

        Assert.Equal(ShellPhase.Normal, h.M.Shell);
        Assert.True(h.State.IsGitHubSynced);
        Assert.Equal(DocPhase.Clean, h.M.Doc);
        Assert.Equal("{\"new\":1}", h.Services.Files["gh.json"]);
    }

    [Fact]
    public void GitHubLoad_WithoutDiff_DoesNotConfirm()
    {
        var h = GitHubHarness(s =>
        {
            s.Files["gh.json"] = "{\r\n}";
            s.GetFile = _ => new GitHubFileResult(true, "", "{\n}");
        });

        h.Send(IntentKind.GitHubLoadRequested);

        Assert.Equal(ShellPhase.Normal, h.M.Shell);
        Assert.Null(h.Confirm);
        Assert.True(h.State.IsGitHubSynced);
    }

    [Fact]
    public void GitHubLoad_ChangelogFetchFails_KeepsCommitEnabled()
    {
        var h = GitHubHarness(s =>
        {
            s.ChangelogRemotePath = "log.csv";
            s.GetFile = path => path == "log.csv" ? new GitHubFileResult(false, "boom") : new GitHubFileResult(true, "", "{}");
        });

        h.Send(IntentKind.GitHubLoadRequested);
        h.Send(IntentKind.ConfirmChoiceSelected, 0);

        Assert.False(h.State.IsGitHubSynced);
        Assert.True(h.M.CanExecute(AppCommand.GitHubCommit));
    }

    [Fact]
    public void GitHubLoad_AuthFailure_DeletesToken()
    {
        var h = GitHubHarness(s => s.GetFile = _ => new GitHubFileResult(false, "401") { AuthFailed = true });
        h.Send(IntentKind.GitHubLoadRequested);
        Assert.True(h.Services.TokenDeleted);
        Assert.Equal(ShellPhase.Normal, h.M.Shell);
    }

    [Fact]
    public void GitHubCommit_Succeeds_AndSyncs()
    {
        var h = GitHubHarness();
        h.OpenDictionary(MediatorHarness.NewWord(1, "a"));
        h.Services.Files["d.json"] = "{}";
        OpenNewWordEditor(h, "b");
        h.Send(IntentKind.WordCommitted);

        h.Send(IntentKind.GitHubCommitRequested);
        var dialog = Assert.IsType<CommitViewModel>(h.State.ModalOverlay);
        Assert.Contains("b", dialog.Message);
        dialog.Message = "  ";
        h.Send(IntentKind.GitHubCommitConfirmed);

        var commit = Assert.Single(h.Services.Commits);
        Assert.Equal(dialog.DefaultMessage, commit.Message);
        Assert.True(h.State.IsGitHubSynced);
        Assert.False(h.M.CanExecute(AppCommand.GitHubCommit));
        Assert.Equal(DocPhase.Clean, h.M.Doc);
    }

    [Fact]
    public void TextEquals_IgnoresLineEndings()
    {
        Assert.True(AppMediator.TextEquals("a\r\nb", "a\nb"));
        Assert.False(AppMediator.TextEquals("a\nb", "a\nc"));
    }
}
