using System.Windows;
using System.Windows.Documents;
using ZasDictWin.Services;

namespace ZasDictWin.Tests;

public class MarkdownRendererTests
{
    private static string Text(Inline inline) => inline switch
    {
        Run r => r.Text,
        LineBreak => "\n",
        Span s => string.Concat(s.Inlines.Select(Text)),
        _ => "",
    };

    private static string Text(Paragraph p) => string.Concat(p.Inlines.Select(Text));

    private static IEnumerable<Inline> All(InlineCollection inlines)
        => inlines.SelectMany(i => i is Span s ? new[] { i }.Concat(All(s.Inlines)) : new[] { i });

    private static Hyperlink OnlyLink(string md)
    {
        var p = (Paragraph)Markdown.ToFlowDocument(md).Blocks.Single();
        return All(p.Inlines).OfType<Hyperlink>().Single();
    }

    [Theory]
    [InlineData("[a](https://example.com/x)", "https://example.com/x")]
    [InlineData("[a](http://example.com)", "http://example.com/")]
    [InlineData("<me@example.com>", "mailto:me@example.com")]
    public void Link_AllowedSchemes_AreNavigable(string md, string uri) => Sta.Run(()
        => Assert.Equal(uri, OnlyLink(md).NavigateUri!.AbsoluteUri));

    // 構文としては通るが、既定のブラウザ以外を起動しうる行き先。押しても何も起こらないリンクにする。
    [Theory]
    [InlineData("[a](ftp://example.com)")]
    [InlineData("[a](data:text/html,x)")]
    [InlineData("[a](relative/page.html)")]
    [InlineData("[a](ms-settings:privacy)")]
    public void Link_OtherSchemes_HaveNoTarget(string md) => Sta.Run(()
        => Assert.Null(OnlyLink(md).NavigateUri));

    [Fact]
    public void Headings_ScaleWithLevelAndFactor() => Sta.Run(() =>
    {
        var doc = Markdown.ToFlowDocument("# h1\n###### h6\nbody", 2.0);
        var blocks = doc.Blocks.Cast<Paragraph>().ToList();

        Assert.Equal(28, doc.FontSize);
        Assert.Equal(14 * 1.7 * 2, blocks[0].FontSize, 6);
        Assert.Equal(14 * 0.95 * 2, blocks[1].FontSize, 6);
        Assert.Equal(FontWeights.Bold, blocks[0].FontWeight);
        Assert.Equal("body", Text(blocks[2]));
    });

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void NonPositiveScale_FallsBackToOne(double scale) => Sta.Run(()
        => Assert.Equal(14, Markdown.ToFlowDocument("a", scale).FontSize));

    [Fact]
    public void Lists_PrefixMarkersToFirstParagraph() => Sta.Run(() =>
    {
        var bullets = Markdown.ToFlowDocument("- a\n- b").Blocks.Cast<Paragraph>().Select(Text);
        Assert.Equal(new[] { "• a", "• b" }, bullets);

        var ordered = Markdown.ToFlowDocument("3. x\n4. y").Blocks.Cast<Paragraph>().Select(Text);
        Assert.Equal(new[] { "3. x", "4. y" }, ordered);
    });

    [Fact]
    public void NestedList_SharesTheLineWithOuterMarker() => Sta.Run(()
        => Assert.Equal("• • inner", Text((Paragraph)Markdown.ToFlowDocument("- - inner").Blocks.Single())));

    [Fact]
    public void ListItemStartingWithTableOrEmpty_GetsItsOwnMarkerLine() => Sta.Run(() =>
    {
        var blocks = Markdown.ToFlowDocument("- | a |\n  |---|").Blocks.ToList();
        Assert.Equal("• ", Text((Paragraph)blocks[0]));
        Assert.IsType<Table>(blocks[1]);

        Assert.Equal("• ", Text((Paragraph)Markdown.ToFlowDocument("-").Blocks.Single()));
    });

    [Fact]
    public void CodeBlock_KeepsLinesWithoutTrailingBreak() => Sta.Run(()
        => Assert.Equal("a\n  b", Text((Paragraph)Markdown.ToFlowDocument("```\na\n  b\n```").Blocks.Single())));

    [Fact]
    public void Inlines_MapToWpfElements() => Sta.Run(() =>
    {
        var p = (Paragraph)Markdown.ToFlowDocument("**b** *i* ~~s~~ `c`\nsoft  \nhard").Blocks.Single();
        var inlines = All(p.Inlines).ToList();

        Assert.Equal("b", Text(inlines.OfType<Bold>().Single()));
        Assert.Equal("i", Text(inlines.OfType<Italic>().Single()));
        Assert.Contains(inlines.OfType<Span>(), s => s.TextDecorations == TextDecorations.Strikethrough);
        Assert.Single(inlines.OfType<LineBreak>());
        Assert.Equal("b i s c soft\nhard", Text(p));   // 改行は空白、行末の 2 空白は改行
    });

    [Fact]
    public void Quote_DrawsLeftBorderOnItsParagraphs() => Sta.Run(() =>
    {
        var p = (Paragraph)Markdown.ToFlowDocument("> q").Blocks.Single();
        Assert.True(p.BorderThickness.Left > 0);
        Assert.True(p.Margin.Left > 0);
    });

    [Fact]
    public void Table_HeaderIsBoldAndCellsFollowAlignment() => Sta.Run(() =>
    {
        var table = (Table)Markdown.ToFlowDocument("| a | b |\n|---|--:|\n| 1 | |").Blocks.Single();

        Assert.Equal(2, table.RowGroups.Count);
        var head = table.RowGroups[0].Rows[0].Cells.Select(c => (Paragraph)c.Blocks.Single()).ToList();
        var body = table.RowGroups[1].Rows[0].Cells.Select(c => (Paragraph)c.Blocks.Single()).ToList();
        Assert.All(head, p => Assert.Equal(FontWeights.Bold, p.FontWeight));
        Assert.Equal(TextAlignment.Right, body[1].TextAlignment);
        Assert.Equal(" ", Text(body[1]));   // 空のセルも行の高さを保つ
    });

    [Fact]
    public void TableWithoutBody_HasOnlyHeader() => Sta.Run(()
        => Assert.Single(((Table)Markdown.ToFlowDocument("| a |\n|---|").Blocks.Single()).RowGroups));

    [Fact]
    public void ThematicBreak_IsABorderedParagraph() => Sta.Run(()
        => Assert.True(((Paragraph)Markdown.ToFlowDocument("---").Blocks.Single()).BorderThickness.Top > 0));
}
