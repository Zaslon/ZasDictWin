using System.IO;
using ZasDictWin.Models;
using ZasDictWin.Services;

namespace ZasDictWin.Tests;

public class SearchServiceTests
{
    private static SearchService Search(string? ignored = null) => new(new TextProcessor(null, null), ignored);

    private static Word Make(int id, string form, params string[] translations)
    {
        var w = Word.CreateNew(id);
        w.Form = form;
        w.Translations.Add(new Translation { Title = "名詞", Forms = translations.ToList() });
        return w;
    }

    private static string[] Forms(IEnumerable<Word> words) => words.Select(w => w.Form).ToArray();

    [Fact]
    public void EmptyQuery_ReturnsEverything()
    {
        var words = new[] { Make(1, "a"), Make(2, "b") };
        Assert.Same(words, Search().Filter(words, "", SearchMode.Exact, SearchScope.Form));
    }

    [Theory]
    [InlineData(SearchMode.Forward, new[] { "abc", "ab" })]
    [InlineData(SearchMode.Backward, new[] { "xab", "ab" })]
    [InlineData(SearchMode.Partial, new[] { "abc", "xab", "ab" })]
    [InlineData(SearchMode.Exact, new[] { "ab" })]
    public void Mode_DecidesHowTheFormMatches(SearchMode mode, string[] expected)
    {
        var words = new[] { Make(1, "abc"), Make(2, "xab"), Make(3, "ab"), Make(4, "zzz") };
        Assert.Equal(expected, Forms(Search().Filter(words, "ab", mode, SearchScope.Form)));
    }

    [Fact]
    public void Matching_IgnoresCase()
    {
        var words = new[] { Make(1, "Kaiko", "Ship") };
        Assert.Single(Search().Filter(words, "KAI", SearchMode.Forward, SearchScope.Form));
        Assert.Single(Search().Filter(words, "ship", SearchMode.Exact, SearchScope.Translation));
    }

    [Fact]
    public void Scope_FormAndTranslationAreSeparate()
    {
        var words = new[] { Make(1, "ab", "dog"), Make(2, "zz", "ab") };
        Assert.Equal(new[] { "ab" }, Forms(Search().Filter(words, "ab", SearchMode.Exact, SearchScope.Form)));
        Assert.Equal(new[] { "zz" }, Forms(Search().Filter(words, "ab", SearchMode.Exact, SearchScope.Translation)));
        Assert.Equal(new[] { "ab", "zz" }, Forms(Search().Filter(words, "ab", SearchMode.Exact, SearchScope.Both)));
    }

    [Fact]
    public void TranslationScope_AlsoMatchesPartOfSpeech()
    {
        var words = new[] { Make(1, "ab", "dog") };
        Assert.Single(Search().Filter(words, "名詞", SearchMode.Exact, SearchScope.Translation));
    }

    [Fact]
    public void FullText_ReachesVariationsTagsContentsAndRelations()
    {
        var w = Make(1, "base", "dog");
        w.Variations.Add(new Variation { Title = "複数", Form = "varform" });
        w.Tags.Add("tagword");
        w.Contents.Add(new ContentItem { Title = "語法", Text = "in the contents" });
        w.Relations.Add(new Relation { Title = "参照", Id = 2, Form = "relform" });
        var words = new[] { w };

        foreach (var q in new[] { "varform", "tagword", "語法", "contents", "relform" })
        {
            Assert.Single(Search().Filter(words, q, SearchMode.Partial, SearchScope.FullText));
            Assert.Empty(Search().Filter(words, q, SearchMode.Partial, SearchScope.Both));
        }
    }

    [Fact]
    public void IgnoredPattern_IsStrippedFromTargetAndQuery()
    {
        var words = new[] { Make(1, "ab", "【音】hit") };
        var search = Search("【.*?】");

        Assert.Single(search.Filter(words, "hit", SearchMode.Exact, SearchScope.Translation));
        // 無視対象の記号を入力に含めても当たる（検索語からも同じ規則で削る）。
        Assert.Single(search.Filter(words, "【音】hit", SearchMode.Exact, SearchScope.Translation));
        Assert.Empty(Search().Filter(words, "hit", SearchMode.Exact, SearchScope.Translation));
    }

    [Fact]
    public void IgnoredPattern_IsNotAppliedToFullText()
    {
        var words = new[] { Make(1, "ab", "【音】hit") };
        Assert.Empty(Search("【.*?】").Filter(words, "hit", SearchMode.Exact, SearchScope.FullText));
    }

    [Fact]
    public void IgnoredPattern_IgnoresCase()
    {
        var words = new[] { Make(1, "abX") };
        Assert.Single(Search("X").Filter(words, "ab", SearchMode.Exact, SearchScope.Form));
    }

    [Fact]
    public void BrokenIgnoredPattern_IsLoggedAndSearchStillWorks()
    {
        var words = new[] { Make(1, "a(b") };

        var search = Search("(");

        Assert.Single(search.Filter(words, "a(b", SearchMode.Exact, SearchScope.Form));
        Assert.Contains("ignoredPattern", File.ReadAllText(ErrorLog.FilePath));
    }
}
