using System.Text.Json.Nodes;
using ZasDictWin.Models;
using ZasDictWin.Resources;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Tests;

public class ExampleViewModelTests
{
    private static Example Ex(int id, string sentence, string translation = "", string supplement = "", params string[] tags)
    {
        var e = Example.CreateNew(id);
        e.Sentence = sentence;
        e.Translation = translation;
        e.Supplement = supplement;
        e.Tags = tags.ToList();
        return e;
    }

    private static Word W(int id, string form)
    {
        var w = Word.CreateNew(id);
        w.Form = form;
        return w;
    }

    private static OtmDocument Doc(IEnumerable<Word>? words = null, params Example[] examples)
        => new(new JsonObject(), words ?? Array.Empty<Word>(), null, examples);

    private static ExampleEditViewModel Editor(Example? source = null, OtmDocument? doc = null, bool hasKey = false)
        => new(source, doc ?? Doc(), new SearchService(new TextProcessor(null, null), null), hasKey);

    // ---- 一覧 --------------------------------------------------------------------

    [Theory]
    [InlineData("", 4)]
    [InlineData("HELLO", 1)]     // 文。大文字小文字は問わない
    [InlineData("訳", 1)]
    [InlineData("補足", 1)]
    [InlineData("挨拶", 1)]      // タグ
    [InlineData("none", 0)]
    public void Examples_FilterAcrossFields(string query, int count)
    {
        var doc = Doc(null, Ex(1, "hello"), Ex(2, "b", "訳"), Ex(3, "c", "", "補足"), Ex(4, "d", "", "", "挨拶"));

        var vm = new ExamplesViewModel(doc, query);

        Assert.Equal(count, vm.Examples.Count);
        Assert.Equal(count == 0, vm.IsEmpty);
        Assert.Equal(string.Format(Strings.Examples_CountLabel, count, 4), vm.CountLabel);
    }

    [Fact]
    public void Examples_ChangingQueryRefilters()
    {
        var vm = new ExamplesViewModel(Doc(null, Ex(1, "a"), Ex(2, "b")));
        vm.Query = " a ";
        Assert.Single(vm.Examples);
    }

    // ---- 編集（新規）----------------------------------------------------------------

    [Fact]
    public void NewExample_StartsAsSelfMadeAndCannotBeDeletedOrFetched()
    {
        var vm = Editor();

        Assert.Equal(Strings.ExampleEdit_AddTitle, vm.Title);
        Assert.Equal(Strings.ExampleEdit_IdPending, vm.IdLabel);
        Assert.False(vm.CanDelete);
        Assert.Equal(Const.ExampleCatalogSelf, vm.Catalog);
        Assert.False(vm.IsOnlineCatalog);
        Assert.False(vm.CanFetch);
        Assert.Equal(Choices.Current.ExampleCatalogs, vm.Catalogs);
    }

    [Fact]
    public void ExistingExample_IsCopiedIntoTheForm()
    {
        var source = Ex(5, "s", "t", "p", "a", "b");
        source.OfferCatalog = "";
        source.Words.Add(new ExampleWord { Id = 1, Form = "ka" });

        var vm = Editor(source);
        vm.Words[0].Form = "changed";

        Assert.Equal(Strings.ExampleEdit_EditTitle, vm.Title);
        Assert.Equal(string.Format(Strings.ExampleEdit_IdFormat, 5), vm.IdLabel);
        Assert.Equal(("s", "t", "p", "a, b"), (vm.Sentence, vm.TranslationText, vm.Supplement, vm.TagsText));
        Assert.Equal(Const.ExampleCatalogSelf, vm.Catalog);   // 出典が空なら自作扱い
        Assert.Equal("ka", source.Words[0].Form);              // 編集欄は写しを持つ
        Assert.True(vm.CanDelete);
    }

    [Theory]
    [InlineData("5", 5)]
    [InlineData(" 7 ", 7)]
    [InlineData("0", 0)]
    [InlineData("-3", 0)]
    [InlineData("abc", 0)]
    public void OfferNumber_ParsesOnlyPositiveNumbers(string text, int expected)
    {
        var vm = Editor();
        vm.OfferNumberText = text;
        Assert.Equal(expected, vm.OfferNumber);
    }

    [Fact]
    public void CanFetch_NeedsOnlineCatalogPositiveNumberAndIdle()
    {
        var vm = Editor();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.OfferStatus = "old";

        vm.Catalog = "meat";
        Assert.Equal("", vm.OfferStatus);   // 出典を変えたら前の照会結果は消す
        vm.OfferNumberText = "3";
        Assert.True(vm.CanFetch);
        vm.IsFetching = true;
        Assert.False(vm.CanFetch);

        Assert.Contains(nameof(vm.CanFetch), raised);
        Assert.Contains(nameof(vm.SelectedCatalog), raised);
    }

    [Fact]
    public void SelectedCatalog_MapsToApiName()
    {
        var vm = Editor();
        vm.SelectedCatalog = vm.Catalogs.First(c => c.Api == "meat");
        Assert.Equal("meat", vm.Catalog);
        vm.SelectedCatalog = null;
        Assert.Equal("meat", vm.Catalog);
        Assert.Equal("meat", vm.SelectedCatalog!.Api);
    }

    [Fact]
    public void WordCandidates_FollowQueryAndAddingClearsIt()
    {
        var words = Enumerable.Range(1, 40).Select(i => W(i, $"ka{i}")).Append(W(99, "zz")).ToList();
        var vm = Editor(doc: Doc(words));

        vm.WordQuery = "KA";
        Assert.Equal(30, vm.WordCandidates.Count);   // 候補は 30 件まで

        vm.AddWord(words[0]);
        vm.AddWord(words[0]);
        Assert.Single(vm.Words);
        Assert.Equal("", vm.WordQuery);
        Assert.Empty(vm.WordCandidates);

        vm.RemoveWord(vm.Words[0]);
        Assert.Empty(vm.Words);
    }

    [Fact]
    public void Validate_RequiresSentence()
    {
        var vm = Editor();
        vm.Sentence = "  ";
        Assert.False(vm.Validate());
        Assert.Equal(Strings.ExampleEdit_ValidationSentence, vm.ValidationMessage);

        vm.Sentence = "s";
        Assert.True(vm.Validate());
        Assert.Equal("", vm.ValidationMessage);
    }

    [Fact]
    public void ApplyTo_TrimsSplitsTagsAndUsesOwnIdForSelfMade()
    {
        var vm = Editor();
        vm.Sentence = " s ";
        vm.TagsText = "a, b、c，, ";
        vm.OfferNumberText = "9";
        var target = Example.CreateNew(12);

        vm.ApplyTo(target);

        Assert.Equal("s", target.Sentence);
        Assert.Equal(new[] { "a", "b", "c" }, target.Tags);
        Assert.Equal(12, target.OfferNumber);
        Assert.Equal(12, target.Raw["offer"]!["number"]!.GetValue<int>());   // Raw にも書き戻す
    }

    [Fact]
    public void ApplyTo_OnlineCatalogKeepsOfferNumber()
    {
        var vm = Editor();
        vm.Sentence = "s";
        vm.Catalog = "meat";
        vm.OfferNumberText = "9";
        var target = Example.CreateNew(12);

        vm.ApplyTo(target);

        Assert.Equal(("meat", 9), (target.OfferCatalog, target.OfferNumber));
    }

    [Fact]
    public void ApiKey_InputAndHint()
    {
        var vm = Editor();
        Assert.False(vm.CanSaveApiKey);
        vm.ApiKeyInput = " k ";
        Assert.True(vm.CanSaveApiKey);

        Assert.Equal(string.Format(Strings.Zpdic_KeyHintMissing, ZpdicApi.ApiKeyPath), vm.ApiKeyHint);
        vm.HasApiKey = true;
        Assert.Equal(string.Format(Strings.Zpdic_KeyHintSaved, ZpdicApi.ApiKeyPath), vm.ApiKeyHint);
    }
}
