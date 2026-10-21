using ZasDictWin.Services;

namespace ZasDictWin.Tests;

public class EtymologyTests
{
    private static string Show(string text)
        => string.Join(" ", Etymology.Split(text).Select(s => s.IsIdyerin ? $"<{s.Text}>" : $"[{s.Text}]"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_HasNoSegments(string? text) => Assert.Empty(Etymology.Split(text));

    // <…> がイジェール文字で描く範囲、[…] がそれ以外。例はどれも Etymology の説明にある実データの形。
    [Theory]
    [InlineData("dos+icen", "<dos> [+] <icen>")]
    [InlineData("cal/mo+aker", "[cal/] <mo> [+] <aker>")]
    [InlineData("*nes+for|足の重ねる所", "[*] <nes> [+] <for> [|足の重ねる所]")]
    [InlineData("ru:Кобальт", "[ru:Кобальт]")]
    [InlineData("a:>i.t:", "[a:>i.t:]")]
    [InlineData("nudi>", "<nudi> [>]")]
    public void Split_DocumentedForms(string text, string expected) => Assert.Equal(expected, Show(text));

    [Theory]
    [InlineData("i:sAna", "[i:] <sAna>")]
    [InlineData("i.s:ab", "[i.s:] <ab>")]
    [InlineData("i.r:ab", "[i.r:] <ab>")]       // 凡例に無い下位区分も i で始まればイジェール語
    [InlineData("en:apple", "[en:apple]")]
    [InlineData("ix:apple", "[ix:apple]")]      // i で始まっても下位区分の形でなければ別の言語
    public void LanguageCode_DecidesTheScript(string text, string expected) => Assert.Equal(expected, Show(text));

    [Fact]
    public void LongPrefix_IsNotALanguageCode()
        => Assert.Equal("<abcde> [:] <xyz>", Show("abcde:xyz"));

    [Fact]
    public void NonAsciiCreator_IsNotStripped()
        => Assert.Equal("[かりぐら/] <mo>", Show("かりぐら/mo"));

    [Fact]
    public void QuotesAndKanaInsideIdyerinPart_StayInPlainScript()
        => Assert.Equal("[「] <ab> [」]", Show("「ab」"));

    [Theory]
    [InlineData("dos+icen")]
    [InlineData("cal/mo+aker>|原義>|変化後")]
    [InlineData("ru:Кобальт+i:ab|意味")]
    [InlineData("++>>")]
    public void Segments_ConcatenateBackToInput_AndAlternate(string text)
    {
        var segments = Etymology.Split(text);
        Assert.Equal(text, string.Concat(segments.Select(s => s.Text)));
        Assert.All(segments.Zip(segments.Skip(1)), pair => Assert.NotEqual(pair.First.IsIdyerin, pair.Second.IsIdyerin));
    }
}
