using System.IO;
using System.Text.Json.Nodes;
using ZasDictWin.Mediator;
using ZasDictWin.Models;
using ZasDictWin.Resources;
using ZasDictWin.Root;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Tests;

public class OverlayViewModelTests
{
    private static SettingsViewModel Settings(AppSettings? settings = null, OtmDocument? doc = null)
        => new(settings ?? new AppSettings(), doc, new Dictionary<string, string> { ["a"] = "b", ["c"] = "c" }, false);

    [Fact]
    public void Settings_CopiesValuesAndFormatsRelations()
    {
        var vm = Settings(new AppSettings { GitHubBranch = " ", Mode = EditMode.GitHub });

        Assert.Equal("main", vm.GitHubBranch);
        Assert.True(vm.IsGitHubMode);
        Assert.Equal($"a=b{Environment.NewLine}c=c", vm.ReciprocalText);
        Assert.False(vm.HasDictionary);
        Assert.Equal(Strings.Settings_CurrentFileNone, vm.CurrentFileMissingText);
    }

    [Fact]
    public void Settings_ReadsSearchOptionsFromTheDictionary()
    {
        var doc = new OtmDocument(new JsonObject(), Array.Empty<Word>(), null);
        doc.ZpdicOnline["punctuations"] = new JsonArray("'", "-");
        doc.ZpdicOnline["ignoredPattern"] = "【.*?】";

        var vm = Settings(doc: doc);

        Assert.Equal(("'-", "【.*?】"), (vm.Punctuations, vm.IgnoredPattern));
        Assert.True(vm.HasDictionary);
        Assert.False(vm.HasCurrentFile);
        Assert.Equal(Strings.Settings_CurrentFileUnsaved, vm.CurrentFileMissingText);
    }

    [Fact]
    public void Settings_ResetReciprocalUsesDefaults()
    {
        var vm = Settings();
        vm.ResetReciprocal();
        Assert.Contains("上位語=下位語", vm.ReciprocalText);
    }

    [Fact]
    public void Settings_MissingHeksaFontIsReported()
    {
        var vm = Settings(new AppSettings { HeksaEnabled = true, HeksaFontPath = @"Z:\no\such.ttf" });

        Assert.True(vm.IsFontMissing);
        Assert.False(vm.HasPreviewFont);
        Assert.Same(HeadwordFontState.Fallback, vm.PreviewFontFamily);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.HeksaFontSource = HeksaFontSource.Installed;
        Assert.False(vm.IsHeksaFromFile);
        Assert.Contains(nameof(vm.IsHeksaFromFile), raised);
        Assert.Contains(nameof(vm.IsFontMissing), raised);
    }

    [Fact]
    public void Settings_TokenInputAndHint()
    {
        var vm = Settings();
        Assert.False(vm.CanSaveGitHubToken);
        vm.GitHubTokenInput = " t ";
        Assert.True(vm.CanSaveGitHubToken);

        Assert.Equal(string.Format(Strings.GitHub_TokenHintMissing, GitHubApi.TokenPath), vm.GitHubTokenHint);
        vm.HasGitHubToken = true;
        Assert.Equal(string.Format(Strings.GitHub_TokenHintSaved, GitHubApi.TokenPath), vm.GitHubTokenHint);
    }

    [Fact]
    public void Settings_ModeChangeRaisesIsGitHubMode()
    {
        var vm = Settings();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.Mode = EditMode.GitHub;
        Assert.Contains(nameof(vm.IsGitHubMode), raised);
        Assert.Equal(Languages.All, vm.AvailableLanguages);
    }

    [Fact]
    public void Choice_NumbersItsChoices()
    {
        var vm = new ChoiceViewModel(new ConfirmSpec("T", "M", new[] { new ConfirmChoice("yes", true), new ConfirmChoice("no", false) }));
        Assert.Equal(("T", "M"), (vm.Title, vm.Message));
        Assert.Equal(new[] { new ChoiceItem("yes", true, 0), new ChoiceItem("no", false, 1) }, vm.Choices);
    }

    [Fact]
    public void BrowserTab_HidesContentWhileModalIsShown()
    {
        var main = new MainViewModel(new AppSettings { BrowserVisible = true }, () => { });
        var tab = new BrowserTabViewModel(main, main.Browser);
        var raised = 0;
        tab.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(tab.IsContentShown)) raised++; };
        Assert.True(tab.IsContentShown);

        main.ModalOverlay = new CommitViewModel("s", "m");

        Assert.False(tab.IsContentShown);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Main_WindowTitleShowsDictionaryAndDirtyMark()
    {
        var h = new MediatorHarness();
        Assert.Equal($"ZasDict for Windows: {Strings.Main_NoDictionaryName}", h.State.WindowTitle);
        Assert.Equal("", h.State.CountLabel);

        h.OpenDictionary(MediatorHarness.NewWord(1, "a"));
        Assert.Equal("ZasDict for Windows: d", h.State.WindowTitle);
        Assert.Equal(string.Format(Strings.Main_CountLabel, 1, 1), h.State.CountLabel);

        h.Send(IntentKind.NewWordRequested);
        h.Open<WordEditViewModel>()!.Form = "b";
        h.Send(IntentKind.WordCommitted);
        Assert.EndsWith(" *", h.State.WindowTitle);
        Assert.Equal(string.Format(Strings.Main_WordCountLabel, 2), h.State.WordCountLabel);
    }
}

public class HeadwordFontTests
{
    private static string SystemFont(string file) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"Z:\no\such.ttf")]
    public void Load_MissingPath_IsNull(string? path) => Assert.Null(HeadwordFontState.Load(path));

    [Fact]
    public void Load_NonFontFile_IsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), "ZasDictWinTests_" + Guid.NewGuid().ToString("N") + ".ttf");
        File.WriteAllText(path, "not a font");
        try { Assert.Null(HeadwordFontState.Load(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_FontFile_ReturnsItsFamily()
        => Assert.Contains("Arial", HeadwordFontState.Load(SystemFont("arial.ttf"))!.Source);

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    [InlineData("No Such Font Family 12345")]
    public void LoadInstalled_UnknownName_IsNull(string? name) => Assert.Null(HeadwordFontState.LoadInstalled(name));

    [Fact]
    public void LoadInstalled_MatchesIgnoringCase()
    {
        Assert.NotNull(HeadwordFontState.LoadInstalled(" arial "));
        Assert.NotNull(HeadwordFontState.Load(HeksaFontSource.Installed, @"Z:\ignored.ttf", "Arial"));
    }

    [Fact]
    public void InstalledNames_AreSortedAndDistinct()
    {
        var names = HeadwordFontState.InstalledNames();
        Assert.Equal(names.Order(StringComparer.OrdinalIgnoreCase), names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void FieldFamily_GetsRoomForFallbackWithAbsoluteTarget()
    {
        var state = HeadwordFontState.Instance;
        try
        {
            // Arial は Yu Gothic UI より行が低いので、入力欄用には合成フォントで底上げする。
            var arial = HeadwordFontState.Load(SystemFont("arial.ttf"))!;
            state.Family = arial;

            Assert.NotSame(arial, state.FieldFamily);
            Assert.True(state.FieldFamily.LineSpacing >= HeadwordFontState.Fallback.LineSpacing);
            var target = state.FieldFamily.FamilyMaps.Single().Target;
            Assert.StartsWith("file:///", target);   // 相対の綴りのままだと黙って既定のフォントになる

            state.Family = HeadwordFontState.Fallback;
            Assert.Same(HeadwordFontState.Fallback, state.FieldFamily);
        }
        finally { state.Family = HeadwordFontState.Fallback; }
    }
}
