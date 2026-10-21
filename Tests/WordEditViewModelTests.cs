using System.Collections.ObjectModel;
using ZasDictWin.Models;
using ZasDictWin.Resources;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Tests;

public class WordEditViewModelTests
{
    private static readonly RelationService Relations = new(new Dictionary<string, string>
    {
        ["上位語"] = "下位語",
        ["参照"] = "参照",
    });

    private static WordEditViewModel Editor(Word? source = null, IEnumerable<Word>? all = null, string initialForm = "")
        => new(source, new ObservableCollection<Word>(all ?? Array.Empty<Word>()), Relations,
            new SearchService(new TextProcessor(null, null), null), initialForm);

    private static Word W(int id, string form)
    {
        var w = Word.CreateNew(id);
        w.Form = form;
        return w;
    }

    [Fact]
    public void New_StartsWithOneEmptyTranslationRowAndTrimmedForm()
    {
        var vm = Editor(initialForm: "  ka ");

        Assert.Equal(Strings.WordEdit_AddTitle, vm.Title);
        Assert.Equal("ka", vm.Form);
        Assert.Single(vm.Translations);
        Assert.Equal(Choices.Current.ContentTypes, vm.AvailableContentTypes);
        Assert.False(vm.HasChanges);
    }

    [Fact]
    public void Existing_OrdersContentsByChoicesAndUnknownLast()
    {
        var w = W(1, "ka");
        w.Contents.Add(new ContentItem { Title = "独自の欄", Text = "x" });
        w.Contents.Add(new ContentItem { Title = "語源", Text = "y" });
        w.Contents.Add(new ContentItem { Title = "語法", Text = "z" });
        w.Relations.Add(new Relation { Title = "上位語", Id = 2, Form = "b" });

        var vm = Editor(w);

        Assert.Equal(new[] { "語法", "語源", "独自の欄" }, vm.Contents.Select(c => c.Title));
        Assert.DoesNotContain("語源", vm.AvailableContentTypes);
        Assert.Equal("下位語", vm.Relations[0].CounterpartHint);
    }

    [Fact]
    public void HasChanges_ComparesBuiltContentNotRawRows()
    {
        var vm = Editor(W(1, "ka"));
        Assert.False(vm.HasChanges);

        vm.AddTranslationRow();     // 空の行は保存されないので変更ではない
        vm.AddVariationRow();
        Assert.False(vm.HasChanges);

        vm.Form = "ki";
        Assert.True(vm.HasChanges);
        vm.Form = " ka ";
        Assert.False(vm.HasChanges);
    }

    [Fact]
    public void Validate_RequiresKnownPartOfSpeechOnNonEmptyRows()
    {
        var vm = Editor();
        Assert.True(vm.Validate());   // 空の行だけなら通す

        vm.Translations[0].FormsText = "船";
        Assert.False(vm.Validate());
        Assert.Equal(Strings.WordEdit_ValidationPos, vm.ValidationMessage);

        vm.Translations[0].Title = "架空の品詞";
        Assert.False(vm.Validate());

        vm.Translations[0].Title = " 名詞 ";
        Assert.True(vm.Validate());
        Assert.Equal("", vm.ValidationMessage);
    }

    [Fact]
    public void Build_SplitsListsAndSkipsEmptyRows()
    {
        var vm = Editor();
        vm.TagsText = "a, b、c，";
        vm.Translations[0].Title = " 名詞 ";
        vm.Translations[0].FormsText = "船、 艦";
        vm.AddTranslationRow();
        vm.AddContentType("語法");
        vm.AddVariationRow();
        vm.Variations[0].Title = " 複数 ";
        vm.Variations[0].Form = " kas ";
        vm.AddVariationRow();

        Assert.Equal(new[] { "a", "b", "c" }, vm.BuildTags());
        var t = Assert.Single(vm.BuildTranslations());
        Assert.Equal("名詞", t.Title);
        Assert.Equal(new[] { "船", "艦" }, t.Forms);
        Assert.Equal("語法", Assert.Single(vm.BuildContents()).Title);   // 題だけの欄は残す
        var v = Assert.Single(vm.BuildVariations());
        Assert.Equal(("複数", "kas"), (v.Title, v.Form));
    }

    [Fact]
    public void SpecialPronTag_FollowsPronunciationContent()
    {
        var vm = Editor();
        vm.TagsText = Const.SpecialPronTag;
        Assert.Empty(vm.BuildTags());   // 発音記号の欄が空なら外す

        vm.AddContentType(Const.PronContentTitle);
        vm.Contents.Single(c => c.Title == Const.PronContentTitle).Text = "ka";
        vm.TagsText = "x";
        Assert.Equal(new[] { "x", Const.SpecialPronTag }, vm.BuildTags());
    }

    [Fact]
    public void ContentTypes_AreAddedOnceInChoicesOrder()
    {
        var vm = Editor();
        vm.AddContentType("語源");
        vm.AddContentType("語法");
        vm.AddContentType("語法");

        Assert.Equal(new[] { "語法", "語源" }, vm.Contents.Select(c => c.Title));
        Assert.DoesNotContain("語法", vm.AvailableContentTypes);

        vm.RemoveContentRow(vm.Contents[0]);
        Assert.Contains("語法", vm.AvailableContentTypes);
    }

    [Fact]
    public void RelationCandidates_ExcludeSelfAndAddingClearsQuery()
    {
        var self = W(1, "kaiko");
        var other = W(2, "kaisa");
        var vm = Editor(self, new[] { self, other });
        vm.RelationTitle = "上位語";

        vm.RelationQuery = "KAI";
        Assert.Equal(new[] { other }, vm.RelationCandidates);

        vm.AddRelation(other);
        vm.AddRelation(other);
        Assert.Single(vm.Relations);
        Assert.Equal(("上位語", 2, "kaisa", "下位語"), (vm.Relations[0].Title, vm.Relations[0].Id, vm.Relations[0].Form, vm.Relations[0].CounterpartHint));
        Assert.Equal("", vm.RelationQuery);
        Assert.Empty(vm.RelationCandidates);

        vm.RelationTitle = "参照";
        vm.AddRelation(other);   // 関係名が違えば同じ相手でも足せる
        Assert.Equal(2, vm.BuildRelations().Count);

        vm.RemoveRelationRow(vm.Relations[0]);
        Assert.Single(vm.Relations);
    }

    [Fact]
    public void RelationTitleHint_ShowsCounterpartOrNone()
    {
        var vm = Editor();
        vm.RelationTitle = "上位語";
        Assert.Equal(string.Format(Strings.WordEdit_ReciprocalHint, "下位語"), vm.RelationTitleHint);
        vm.RelationTitle = "未登録";
        Assert.Equal(Strings.WordEdit_NoReciprocalHint, vm.RelationTitleHint);
    }

    [Fact]
    public void DefaultRelationTitle_HasNoEnTags()
        => Assert.Equal(EnTag.Strip(Strings.Relation_DefaultTitle), Editor().RelationTitle);

    [Fact]
    public void RowsCanBeRemoved()
    {
        var vm = Editor();
        vm.AddVariationRow();
        vm.RemoveTranslationRow(vm.Translations[0]);
        vm.RemoveVariationRow(vm.Variations[0]);
        Assert.Empty(vm.Translations);
        Assert.Empty(vm.Variations);
    }
}
