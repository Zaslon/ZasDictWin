using ZasDictWin.Models;
using ZasDictWin.Services;

namespace ZasDictWin.Tests;

public class TextProcessorTests
{
    private static readonly TextProcessor Default = new(null, null);

    [Fact]
    public void EmptySortOrder_FallsBackToDefault()
    {
        Assert.Equal(TextProcessor.DefaultSortOrder, Default.SortOrder);
        Assert.Equal(TextProcessor.DefaultSortOrder, new TextProcessor("", "").SortOrder);
    }

    [Theory]
    [InlineData("e", "a")]      // 字順表の先頭が e、次が a
    [InlineData("a", "o")]
    [InlineData("h", "k")]
    [InlineData("v", "x")]      // 字順表に無い文字は表の後ろ
    [InlineData("x", "y")]      // 表に無い文字どうしはコードポイント順
    [InlineData("ka", "kaa")]   // 前方一致なら短いほうが先
    public void CompareForms_FollowsSortOrder(string earlier, string later)
    {
        Assert.True(Default.CompareForms(earlier, later) < 0);
        Assert.True(Default.CompareForms(later, earlier) > 0);
    }

    [Fact]
    public void CompareForms_IgnoresCaseAndPunctuations()
    {
        Assert.Equal(0, Default.CompareForms("Kaiko", "kaiko"));
        var withPunct = new TextProcessor(null, "'.");
        Assert.Equal(0, withPunct.CompareForms("s'i.a", "sia"));
        Assert.NotEqual(0, Default.CompareForms("s'ia", "sia"));
    }

    [Fact]
    public void Normalize_LongInput_StripsPunctuations()
    {
        // 256 文字を超えるとスタックではなくヒープの作業域を使う。
        var tp = new TextProcessor(null, "-");
        var input = string.Concat(Enumerable.Repeat("A-", 200));
        Assert.Equal(new string('a', 200), tp.Normalize(input));
    }

    [Fact]
    public void WordComparer_SameForm_OrdersById()
    {
        var w5 = Word.CreateNew(5); w5.Form = "ka";
        var w2 = Word.CreateNew(2); w2.Form = "ka";
        var w9 = Word.CreateNew(9); w9.Form = "e";
        var sorted = new[] { w5, w2, w9 }.Order(Default.WordComparer).Select(w => w.Id);
        Assert.Equal(new[] { 9, 2, 5 }, sorted);
    }

    [Fact]
    public void AssignHomonymIndexes_NumbersRepeatsInGivenOrder()
    {
        var words = new[] { "ka", "e", "ka", "ka" }.Select((f, i) =>
        {
            var w = Word.CreateNew(i + 1);
            w.Form = f;
            return w;
        }).ToList();

        TextProcessor.AssignHomonymIndexes(words);

        Assert.Equal(new[] { 1, 1, 2, 3 }, words.Select(w => w.HomonymIndex));
        Assert.Equal(new[] { "ka", "e", "ka (2)", "ka (3)" }, words.Select(w => w.DisplayForm));
    }
}
