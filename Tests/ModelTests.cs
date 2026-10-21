using System.Text.Json.Nodes;
using ZasDictWin.Models;
using ZasDictWin.Resources;

namespace ZasDictWin.Tests;

public class ModelTests
{
    private static JsonObject WordJson() => JsonNode.Parse("""
        {
          "entry": { "id": 3, "form": "kaiko", "extra": "keep-entry" },
          "translations": [ { "title": "名詞", "forms": ["船", "艦"] }, { "title": "", "forms": ["ふね"] }, { "title": "動詞", "forms": [] } ],
          "tags": ["t1"],
          "contents": [ { "title": "語法", "text": "本文" } ],
          "variations": [ { "title": "複数", "form": "kaikos" } ],
          "relations": [ { "title": "類義語", "entry": { "id": 4, "form": "sekore" } } ],
          "zpdic": { "unknown": true }
        }
        """)!.AsObject();

    // ---- Word ------------------------------------------------------------------

    [Fact]
    public void Word_FromJson_ReadsEveryField()
    {
        var w = Word.FromJson(WordJson());

        Assert.Equal((3, "kaiko"), (w.Id, w.Form));
        Assert.Equal(new[] { "船", "艦" }, w.Translations[0].Forms);
        Assert.Equal(new[] { "t1" }, w.Tags);
        Assert.Equal(("語法", "本文"), (w.Contents[0].Title, w.Contents[0].Text));
        Assert.Equal(("複数", "kaikos"), (w.Variations[0].Title, w.Variations[0].Form));
        Assert.Equal(("類義語", 4, "sekore"), (w.Relations[0].Title, w.Relations[0].Id, w.Relations[0].Form));
    }

    [Fact]
    public void Word_FromJson_MissingFieldsAreEmpty()
    {
        var w = Word.FromJson(new JsonObject());
        Assert.Equal((0, ""), (w.Id, w.Form));
        Assert.Empty(w.Translations);
        Assert.Empty(w.Relations);
    }

    [Fact]
    public void Word_TranslationSummary_SkipsEmptyAndBracketsTitles()
        => Assert.Equal("［名詞］船, 艦  ふね", Word.FromJson(WordJson()).TranslationSummary);

    [Fact]
    public void Word_WriteBack_KeepsUnknownKeys()
    {
        var w = Word.FromJson(WordJson());
        w.Form = "renamed";
        w.Tags.Add("t2");

        w.WriteBack();

        Assert.Equal("renamed", w.Raw["entry"]!["form"]!.GetValue<string>());
        Assert.Equal("keep-entry", w.Raw["entry"]!["extra"]!.GetValue<string>());
        Assert.True(w.Raw["zpdic"]!["unknown"]!.GetValue<bool>());
        Assert.Equal(2, w.Raw["tags"]!.AsArray().Count);
    }

    [Fact]
    public void Word_WriteBack_CreatesEntryWhenMissing()
    {
        var w = Word.FromJson(new JsonObject());
        w.Id = 8;
        w.WriteBack();
        Assert.Equal(8, w.Raw["entry"]!["id"]!.GetValue<int>());
    }

    [Fact]
    public void Word_Duplicate_GetsNewIdDropsRelationsAndIsIndependent()
    {
        var original = Word.FromJson(WordJson());

        var copy = original.Duplicate(10);
        copy.Translations[0].Forms.Add("x");
        copy.WriteBack();

        Assert.Equal((10, "kaiko"), (copy.Id, copy.Form));
        Assert.Empty(copy.Relations);
        Assert.Empty(copy.Raw["relations"]!.AsArray());
        Assert.True(copy.Raw["zpdic"]!["unknown"]!.GetValue<bool>());
        Assert.Single(original.Relations);
        Assert.Equal(2, original.Raw["translations"]![0]!["forms"]!.AsArray().Count);
    }

    [Fact]
    public void Word_NotifyChanged_RefreshesEveryBinding()
    {
        var w = Word.CreateNew(1);
        string? name = "unset";
        w.PropertyChanged += (_, e) => name = e.PropertyName;
        w.NotifyChanged();
        Assert.Null(name);
    }

    // ---- Example ---------------------------------------------------------------

    private static JsonObject ExampleJson() => JsonNode.Parse("""
        { "id": 3, "sentence": "s", "translation": "t", "supplement": "p",
          "tags": ["挨拶", ""], "words": [ { "id": 12 }, { "form": "no id" }, { "id": 99 } ],
          "offer": { "catalog": "meat", "number": 7 }, "extra": 1 }
        """)!.AsObject();

    [Fact]
    public void Example_FromJson_ReadsFieldsAndSkipsBlanks()
    {
        var e = Example.FromJson(ExampleJson());

        Assert.Equal((3, "s", "t", "p"), (e.Id, e.Sentence, e.Translation, e.Supplement));
        Assert.Equal(new[] { "挨拶" }, e.Tags);
        Assert.Equal(new[] { 12, 99 }, e.Words.Select(w => w.Id));
        Assert.Equal(("meat", 7), (e.OfferCatalog, e.OfferNumber));
        Assert.Equal("挨拶", e.TagSummary);
    }

    [Fact]
    public void Example_WriteBack_WritesOnlyWordIdsAndKeepsUnknownKeys()
    {
        var e = Example.FromJson(ExampleJson());
        e.Words[0].Form = "表示用";

        e.WriteBack();

        Assert.Equal("""[{"id":12},{"id":99}]""", e.Raw["words"]!.ToJsonString());
        Assert.Equal(1, e.Raw["extra"]!.GetValue<int>());
    }

    [Fact]
    public void Example_Preview_FlattensLinesAndCutsAt50()
    {
        var e = Example.CreateNew(1);
        e.Sentence = "  一行目\r\n二行目  ";
        e.Translation = new string('あ', 51);

        Assert.Equal("一行目 二行目", e.SentencePreview);
        Assert.Equal(new string('あ', 50) + "…", e.TranslationPreview);
        e.Translation = new string('あ', 50);
        Assert.Equal(new string('あ', 50), e.TranslationPreview);
    }

    [Fact]
    public void Example_ResolveForms_UsesDisplayFormOrIdPlaceholder()
    {
        var e = Example.FromJson(ExampleJson());
        var word = Word.CreateNew(12);
        word.Form = "ka";
        word.HomonymIndex = 2;

        e.ResolveForms(new Dictionary<int, Word> { [12] = word });

        Assert.Equal(new[] { "ka (2)", "id:99" }, e.Words.Select(w => w.Form));
        Assert.True(e.References(99));
        Assert.False(e.References(13));
    }

    // ---- OtmDocument -----------------------------------------------------------------

    [Fact]
    public void Document_NextIds_StartAtOneAndFollowMax()
    {
        var empty = new OtmDocument(new JsonObject(), Array.Empty<Word>(), null);
        Assert.Equal((1, 1), (empty.NextId(), empty.NextExampleId()));

        var doc = new OtmDocument(new JsonObject(), new[] { Word.CreateNew(4), Word.CreateNew(2) }, null,
            new[] { Example.CreateNew(7) });
        Assert.Equal((5, 8), (doc.NextId(), doc.NextExampleId()));
    }

    [Fact]
    public void Document_DuplicateWordIds_FirstWinsWithoutThrowing()
    {
        var first = Word.CreateNew(12);
        first.Form = "first";
        var second = Word.CreateNew(12);
        second.Form = "second";

        var doc = new OtmDocument(new JsonObject(), new[] { first, second }, null, new[] { Example.FromJson(ExampleJson()) });

        Assert.Equal("first", doc.Examples[0].Words[0].Form);
        Assert.Single(doc.ExamplesFor(12));
        Assert.Empty(doc.ExamplesFor(13));
    }

    [Fact]
    public void Document_ZpdicOnline_IsCreatedOnceAndReused()
    {
        var doc = new OtmDocument(new JsonObject(), Array.Empty<Word>(), null);
        var z = doc.ZpdicOnline;
        Assert.Same(z, doc.ZpdicOnline);
        Assert.Same(z, doc.Root["zpdicOnline"]);
    }

    [Fact]
    public void Document_Name_IsFileNameOrUntitled()
    {
        Assert.Equal(Strings.Doc_Untitled, new OtmDocument(new JsonObject(), Array.Empty<Word>(), null).Name);
        Assert.Equal("idyer", new OtmDocument(new JsonObject(), Array.Empty<Word>(), @"C:\d\idyer.json").Name);
    }
}
