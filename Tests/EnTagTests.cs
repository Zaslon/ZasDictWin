using ZasDictWin.Services;

namespace ZasDictWin.Tests;

public class EnTagTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_HasNoSegments(string? text) => Assert.Empty(EnTag.Split(text));

    [Fact]
    public void Split_MarksOnlyTaggedRunsAsEn()
    {
        Assert.Equal(new[]
        {
            new EnTagSegment("保存先は ", false),
            new EnTagSegment("GitHub", true),
            new EnTagSegment(" と ", false),
            new EnTagSegment("ZpDIC", true),
        }, EnTag.Split("保存先は [en]GitHub[/en] と [en]ZpDIC[/en]"));
    }

    [Fact]
    public void Split_EmptyTag_LeavesNoEmptySegment()
        => Assert.Equal(new[] { new EnTagSegment("a", false), new EnTagSegment("b", false) }, EnTag.Split("a[en][/en]b"));

    [Fact]
    public void Split_UnclosedTag_KeepsTheRestAsPlainText()
    {
        // 壊れた入力でも本文を失わない。
        Assert.Equal(new[]
        {
            new EnTagSegment("a ", false),
            new EnTagSegment("[en]b", false),
        }, EnTag.Split("a [en]b"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("plain", "plain")]
    [InlineData("x [en]Y[/en] z", "x Y z")]
    [InlineData("[en]broken", "[en]broken")]
    public void Strip_RemovesTagsOnly(string? text, string expected) => Assert.Equal(expected, EnTag.Strip(text));
}
