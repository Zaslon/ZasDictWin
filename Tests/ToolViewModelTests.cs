using System.Text.Json.Nodes;
using ZasDictWin.Models;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Tests;

public class ToolViewModelTests
{
    private static Word W(int id, string form, string[] tags, params string[] pos)
    {
        var w = Word.CreateNew(id);
        w.Form = form;
        w.Tags.AddRange(tags);
        foreach (var p in pos) w.Translations.Add(new Translation { Title = p, Forms = new List<string> { "x" } });
        return w;
    }

    [Fact]
    public void Ipa_ConvertsOnInput()
    {
        var vm = new IpaToolViewModel();
        vm.IpaInput = "ˈæpəl";
        Assert.Equal("ep(e|o)l", vm.IpaSpelling);
    }

    [Fact]
    public void Stats_NoDocument_IsEmpty()
    {
        var vm = new StatsViewModel(null);
        Assert.Equal((0, 0, 0, 0.0), (vm.WordCount, vm.HomonymCount, vm.ExampleCount, vm.AverageFormLength));
        Assert.Empty(vm.TagItems);
    }

    [Fact]
    public void Stats_CountsAndOrdersBreakdowns()
    {
        var doc = new OtmDocument(new JsonObject(), new[]
        {
            W(1, "ka", new[] { "b", "a" }, "名詞"),
            W(2, "ka", new[] { "a" }, "動詞", ""),
            W(3, "sekore", new[] { "c" }, "名詞"),
        }, null, new[] { Example.CreateNew(1) });

        var vm = new StatsViewModel(doc);

        Assert.Equal((3, 1, 1), (vm.WordCount, vm.HomonymCount, vm.ExampleCount));
        Assert.Equal(3.33, vm.AverageFormLength);
        // 件数の多い順、同数なら名前順。
        Assert.Equal(new[] { new BreakdownItem("a", 2), new BreakdownItem("b", 1), new BreakdownItem("c", 1) }, vm.TagItems);
        Assert.Equal(new[] { new BreakdownItem("名詞", 2), new BreakdownItem("動詞", 1) }, vm.PosItems);
    }

    [Fact]
    public void Legend_ReloadEndsEditingAndDropsRenderedDocument()
    {
        var vm = new LegendViewModel("# a", canEdit: true);
        vm.BeginEdit("# a");
        vm.ValidationMessage = "bad";
        vm.DocumentScale = 1.0;

        vm.Reload("# b", canEdit: false);

        Assert.False(vm.IsEditing);
        Assert.Null(vm.ValidationMessage);
        Assert.False(vm.CanEdit);
        Assert.Equal("# b", vm.LegendMarkdown);
        Assert.Null(vm.Document);
        Assert.True(double.IsNaN(vm.DocumentScale));
    }

    [Fact]
    public void Legend_BeginEditCopiesSource()
    {
        var vm = new LegendViewModel("# a", canEdit: true);
        vm.BeginEdit("src");
        Assert.Equal(("src", "src", true), (vm.Draft, vm.EditSource, vm.IsEditing));
    }
}

public class BrowserViewModelTests
{
    [Theory]
    [InlineData("", BrowserViewModel.FallbackStartUrl)]
    [InlineData("   ", BrowserViewModel.FallbackStartUrl)]
    [InlineData("  https://zaslon.info/  ", "https://zaslon.info/")]
    public void StartUrl_FallsBackAndTrims(string configured, string expected)
    {
        var vm = new BrowserViewModel(new AppSettings { BrowserStartUrl = configured });
        Assert.Equal(expected, vm.StartUrl);
        Assert.Equal(expected, vm.Address);
    }

    [Fact]
    public void SyncWithSettings_OnlyMovesAddressWhileClosed()
    {
        var settings = new AppSettings { BrowserVisible = true, BrowserStartUrl = "https://a/" };
        var vm = new BrowserViewModel(settings);
        Assert.True(vm.IsOpen);
        vm.Address = "https://typed/";
        settings.BrowserStartUrl = "https://b/";

        vm.SyncWithSettings();
        Assert.Equal("https://typed/", vm.Address);

        vm.IsOpen = false;
        vm.SyncWithSettings();
        Assert.Equal("https://b/", vm.Address);
    }

    [Fact]
    public void Reports_UpdateDisplayState()
    {
        var vm = new BrowserViewModel(new AppSettings());

        vm.ReportAddress(null);
        Assert.Equal(BrowserViewModel.FallbackStartUrl, vm.Address);
        vm.ReportAddress("https://x/");
        vm.ReportTitle("T");
        vm.ReportError("boom");
        vm.ReportBusy(true);
        vm.ReportStatus("loading");
        vm.ReportHistoryState(true, false);

        Assert.Equal(("https://x/", true, true, true, "loading"), (vm.Address, vm.HasTitle, vm.HasError, vm.IsBusy, vm.Status));
        Assert.Equal((true, false), (vm.CanGoBack, vm.CanGoForward));

        vm.ReportTitle(null);
        vm.ReportError(null);
        Assert.Equal(("", false, false), (vm.Title, vm.HasTitle, vm.HasError));
    }

    [Fact]
    public void Requests_AreForwardedToTheView()
    {
        var vm = new BrowserViewModel(new AppSettings());
        var seen = new List<string>();
        vm.InitializeRequested += () => seen.Add("init");
        vm.NavigateRequested += url => seen.Add(url);
        vm.BackRequested += () => seen.Add("back");
        vm.ForwardRequested += () => seen.Add("forward");
        vm.ReloadRequested += () => seen.Add("reload");

        vm.RequestInitialize();
        vm.RequestNavigate("https://x/");
        vm.RequestBack();
        vm.RequestForward();
        vm.RequestReload();

        Assert.Equal(new[] { "init", "https://x/", "back", "forward", "reload" }, seen);
    }
}
