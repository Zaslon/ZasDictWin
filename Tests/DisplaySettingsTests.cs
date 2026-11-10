using ZasDictWin.Mediator;
using ZasDictWin.Resources;
using ZasDictWin.Root;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Tests;

public class DisplaySettingsTests : IDisposable
{
    // FontScaleState はアプリ全体で 1 つなので、テストごとに既定へ戻す。
    public void Dispose()
    {
        var s = FontScaleState.Instance;
        s.Density = Density.Standard;
        s.Scale = 1.0;
        s.UiScale = 1.0;
    }

    // ---- 文字スタイルの表 ----------------------------------------------------------------

    [Fact]
    public void TextStyles_NeverGoBelow14()
    {
        foreach (var token in new[] { "display", "h1", "h2", "h3", "body", "dense", "oneline", "mono", "small" })
        foreach (var density in new[] { Density.Standard, Density.Compact })
            Assert.True(TextStyles.Size(token, density) >= 14, $"{token} {density}");
    }

    [Fact]
    public void TextStyles_CompactSmallEqualsCompactBody()
        => Assert.Equal(TextStyles.Size("body", Density.Compact), TextStyles.Size("small", Density.Compact));

    [Fact]
    public void BodySizeChoices_FollowTheDensity()
    {
        Assert.Equal(new[] { 16, 18, 20, 22, 24, 26, 28, 30, 32 }, TextStyles.BodySizeChoices(Density.Standard));
        Assert.Equal(new[] { 14, 16, 18, 20, 22, 24, 26, 28 }, TextStyles.BodySizeChoices(Density.Compact));
    }

    [Theory]
    [InlineData(1.0, Density.Standard, 16)]
    [InlineData(2.0, Density.Standard, 32)]
    [InlineData(1.5, Density.Compact, 20)]
    [InlineData(9.0, Density.Compact, 28)]
    public void NearestBody_SnapsToAChoice(double scale, Density density, int expected)
        => Assert.Equal(expected, TextStyles.NearestBody(scale, density));

    [Fact]
    public void ScaleFontSizeConverter_UsesTokensForTextAndNumbersForSizes()
    {
        var converter = new Views.ScaleFontSizeConverter();
        FontScaleState.Instance.Density = Density.Compact;
        Assert.Equal(14 * 1.5, (double)converter.Convert(1.5, typeof(double), "body", null!));
        Assert.Equal(120 * 1.5, (double)converter.Convert(1.5, typeof(double), "120", null!));
        FontScaleState.Instance.Density = Density.Standard;
        Assert.Equal(16 * 1.5, (double)converter.Convert(1.5, typeof(double), "body", null!));
    }

    [Fact]
    public void ChangingDensity_NotifiesScale()
    {
        var raised = new List<string?>();
        FontScaleState.Instance.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        FontScaleState.Instance.Density = Density.Compact;
        Assert.Contains(nameof(FontScaleState.Scale), raised);
    }

    // ---- 表示の設定の画面 ------------------------------------------------------------------

    private static DisplaySettingsViewModel Open(MediatorHarness h)
    {
        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowDisplaySettings);
        return Assert.IsType<DisplaySettingsViewModel>(h.State.ModalOverlay);
    }

    [Fact]
    public void ChoosingAValue_AppliesAndSavesImmediately()
    {
        var h = new MediatorHarness();
        var vm = Open(h);
        var saves = h.Services.SettingsSaves;

        vm.SelectedUiScale = vm.UiScaleOptions.Single(o => o.Label == "150%");
        Assert.Equal(1.5, h.Services.Settings.UiScale);
        Assert.Equal(1.5, FontScaleState.Instance.UiScale);

        vm.SelectedFontSize = vm.FontSizeOptions.Single(o => o.Label == "24px");
        Assert.Equal(1.5, h.Services.Settings.FontScale);
        Assert.Equal(24, FontScaleState.Instance.Size("body"));
        Assert.True(h.Services.SettingsSaves >= saves + 2);
    }

    [Fact]
    public void SwitchingToCompact_SnapsTheTextSizeOntoTheNewChoices()
    {
        var h = new MediatorHarness(s => s.Settings.FontScale = 2.0);
        var vm = Open(h);

        vm.IsCompactDensity = true;

        Assert.Equal(Density.Compact, h.Services.Settings.Density);
        Assert.Equal("28px", vm.SelectedFontSize!.Label);
        Assert.Equal(28, FontScaleState.Instance.Size("body"));
        Assert.Equal(14 * 2.0, FontScaleState.Instance.Size("small"));
    }

    [Fact]
    public void ChoosingTheDefaultTypeface_ClearsTheSetting()
    {
        var h = new MediatorHarness(s => s.Settings.UiFontFamily = "Meiryo");
        var vm = Open(h);
        vm.SelectedFont = new DisplayOption("default", null);
        Assert.Null(h.Services.Settings.UiFontFamily);
    }

    [Fact]
    public void Escape_ClosesTheDisplaySettings()
    {
        var h = new MediatorHarness();
        Open(h);
        Assert.False(h.M.CanExecute(AppCommand.ShowSettings));

        h.Send(IntentKind.CancelRequested);

        Assert.True(h.M.CanExecute(AppCommand.ShowSettings));
    }

    [Fact]
    public void LegacyScale_IsClampedWhenApplied()
    {
        _ = new MediatorHarness(s => { s.Settings.FontScale = 3.0; s.Settings.UiScale = 0.5; });
        Assert.Equal(2.0, FontScaleState.Instance.Scale);
        Assert.Equal(1.0, FontScaleState.Instance.UiScale);
    }

    // ---- メニューバー ---------------------------------------------------------------------

    [Fact]
    public void MenuLabels_ShowAccessKeyAndEllipsis()
    {
        var spec = new MenuActionSpec("開く", "", IntentKind.OpenDictionaryRequested, null, true, false, true, AccessKey: 'O', Ellipsis: true);
        Assert.Equal("開く(O)…", spec.Label);
        Assert.Equal("終了", new MenuActionSpec("終了", "", IntentKind.WindowCloseRequested, null, true, false, true).Label);
    }

    [Fact]
    public void Menus_GiveEveryItemAnAccessKeyUniqueWithinTheMenu()
    {
        var h = new MediatorHarness();
        foreach (var menu in new[] { h.M.BuildFileMenu(), h.M.BuildEditMenu(), h.M.BuildViewMenu(), h.M.BuildToolsMenu(),
                                     h.M.BuildWindowMenu(), h.M.BuildSettingsMenu(), h.M.BuildHelpMenu() })
        {
            Assert.All(menu, i => Assert.NotEqual('\0', i.AccessKey));
            Assert.Equal(menu.Count, menu.Select(i => i.AccessKey).Distinct().Count());
        }
    }

    [Fact]
    public void ItemsThatAskForMoreInput_EndWithEllipsis()
    {
        var h = new MediatorHarness();
        var withEllipsis = h.M.BuildFileMenu().Concat(h.M.BuildEditMenu()).Concat(h.M.BuildViewMenu()).Concat(h.M.BuildSettingsMenu())
            .Where(i => i.Ellipsis).Select(i => i.Intent).ToHashSet();
        Assert.Contains(IntentKind.OpenDictionaryRequested, withEllipsis);
        Assert.Contains(IntentKind.SaveAsRequested, withEllipsis);
        Assert.Contains(IntentKind.SettingsRequested, withEllipsis);
        Assert.DoesNotContain(IntentKind.SaveRequested, withEllipsis);
        Assert.DoesNotContain(IntentKind.UndoRequested, withEllipsis);
    }

    [Fact]
    public void ShortcutsShownInMenus_MatchTheBoundKeys()
    {
        var h = new MediatorHarness();
        var shortcuts = h.M.BuildFileMenu().Concat(h.M.BuildEditMenu())
            .Where(i => i.Shortcut != "").ToDictionary(i => i.Intent, i => i.Shortcut);
        Assert.Equal("Ctrl+S", shortcuts[IntentKind.SaveRequested]);
        Assert.Equal("Ctrl+O", shortcuts[IntentKind.OpenDictionaryRequested]);
        Assert.Equal("Ctrl+Z", shortcuts[IntentKind.UndoRequested]);
        Assert.Equal("Ctrl+F", shortcuts[IntentKind.FocusQueryBoxRequested]);
    }

    [Fact]
    public void Help_OpensUsageAndShowsVersion()
    {
        var h = new MediatorHarness();
        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowUsage);
        Assert.Equal(new[] { Const.UsageUrl }, h.Services.OpenedUrls);

        h.Send(IntentKind.OpenScreenRequested, AppCommand.ShowAbout);
        Assert.Equal(Strings.Menu_About, h.Confirm?.Title);
    }
}
