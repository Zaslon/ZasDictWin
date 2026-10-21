using System.Text;
using ZasDictWin.Services;

namespace ZasDictWin.Tests;

/// <summary>
/// 構文木を markdown-it の出力に近い HTML へ写して比べる（属性の順や改行は簡略化している）。
/// 期待値は CommonMark / markdown-it の挙動。リストは緩い・締まったの区別を持たないので常に &lt;p&gt; を出す。
/// </summary>
public class MarkdownParserTests
{
    private static string Html(string markdown)
    {
        var sb = new StringBuilder();
        Blocks(sb, MarkdownParser.Parse(markdown));
        return sb.ToString();
    }

    private static string Inline(string markdown)
    {
        var sb = new StringBuilder();
        Inlines(sb, MdInlines.Parse(markdown));
        return sb.ToString();
    }

    private static void Blocks(StringBuilder sb, List<MdNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case MdParagraph p: sb.Append("<p>"); Inlines(sb, p.Inlines); sb.Append("</p>"); break;
                case MdHeading h: sb.Append($"<h{h.Level}>"); Inlines(sb, h.Inlines); sb.Append($"</h{h.Level}>"); break;
                case MdCodeBlock c: sb.Append("<pre>").Append(c.Text).Append("</pre>"); break;
                case MdQuote q: sb.Append("<blockquote>"); Blocks(sb, q.Blocks); sb.Append("</blockquote>"); break;
                case MdThematicBreak: sb.Append("<hr>"); break;
                case MdList l:
                    sb.Append(l.Ordered ? (l.Start == 1 ? "<ol>" : $"<ol start=\"{l.Start}\">") : "<ul>");
                    foreach (var item in l.Items) { sb.Append("<li>"); Blocks(sb, item.Blocks); sb.Append("</li>"); }
                    sb.Append(l.Ordered ? "</ol>" : "</ul>");
                    break;
                case MdTable t:
                    sb.Append("<table>");
                    Row(sb, t.Head, t.Aligns, "th");
                    foreach (var row in t.Body) Row(sb, row, t.Aligns, "td");
                    sb.Append("</table>");
                    break;
            }
        }
    }

    private static void Row(StringBuilder sb, MdTableRow row, List<MdAlignKind> aligns, string tag)
    {
        sb.Append("<tr>");
        for (var i = 0; i < row.Cells.Count; i++)
        {
            var align = aligns[i] == MdAlignKind.None ? "" : $" align=\"{aligns[i].ToString().ToLowerInvariant()}\"";
            sb.Append($"<{tag}{align}>");
            Inlines(sb, row.Cells[i]);
            sb.Append($"</{tag}>");
        }
        sb.Append("</tr>");
    }

    private static void Inlines(StringBuilder sb, List<MdInline> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case MdText t: sb.Append(t.Value); break;
                case MdSoftBreak: sb.Append('\n'); break;
                case MdHardBreak: sb.Append("<br>"); break;
                case MdCodeSpan c: sb.Append("<code>").Append(c.Value).Append("</code>"); break;
                case MdEm e: sb.Append("<em>"); Inlines(sb, e.Children); sb.Append("</em>"); break;
                case MdStrong s: sb.Append("<strong>"); Inlines(sb, s.Children); sb.Append("</strong>"); break;
                case MdStrike s: sb.Append("<s>"); Inlines(sb, s.Children); sb.Append("</s>"); break;
                case MdLink l:
                    sb.Append($"<a href=\"{l.Url}\"").Append(l.Title is null ? "" : $" title=\"{l.Title}\"").Append('>');
                    Inlines(sb, l.Children);
                    sb.Append("</a>");
                    break;
            }
        }
    }

    // ---- インライン ------------------------------------------------------------------

    [Theory]
    [InlineData("*a* **b** ***c***", "<em>a</em> <strong>b</strong> <em><strong>c</strong></em>")]
    [InlineData("_a_ __b__", "<em>a</em> <strong>b</strong>")]
    [InlineData("snake_case_word", "snake_case_word")]                   // 語中の _ は強調にしない
    [InlineData("**a *b* c**", "<strong>a <em>b</em> c</strong>")]
    [InlineData("*unclosed", "*unclosed")]
    [InlineData("~~del~~ ~one~", "<s>del</s> ~one~")]
    public void Emphasis(string md, string expected) => Assert.Equal(expected, Inline(md));

    [Theory]
    [InlineData("これは**強調**です", "これは<strong>強調</strong>です")]
    // 素の CommonMark では、約物（」）の直後で和文が続く ** は閉じにならない。CJK 対応でここが効く。
    [InlineData("**「強調」**の", "<strong>「強調」</strong>の")]
    [InlineData("見出し：*斜体*、次", "見出し：<em>斜体</em>、次")]
    public void Emphasis_NextToCjk(string md, string expected) => Assert.Equal(expected, Inline(md));

    [Theory]
    [InlineData("`code *x*`", "<code>code *x*</code>")]
    [InlineData("`` a ` b ``", "<code>a ` b</code>")]
    [InlineData("`a\nb`", "<code>a b</code>")]
    [InlineData("`unclosed", "`unclosed")]
    public void CodeSpan(string md, string expected) => Assert.Equal(expected, Inline(md));

    [Theory]
    [InlineData(@"\*not em\*", "*not em*")]
    [InlineData(@"\a", @"\a")]                    // 約物以外の前の \ はそのまま
    [InlineData("a  \nb", "a<br>b")]
    [InlineData("a\\\nb", "a<br>b")]
    [InlineData("a\n  b", "a\nb")]
    public void EscapesAndBreaks(string md, string expected) => Assert.Equal(expected, Inline(md));

    [Theory]
    [InlineData("&amp; &copy; &#65; &#x41;", "& © A A")]
    [InlineData("&bogus; &amp", "&bogus; &amp")]
    public void Entities(string md, string expected) => Assert.Equal(expected, Inline(md));

    [Theory]
    [InlineData("[t](http://x.com \"T\")", "<a href=\"http://x.com\" title=\"T\">t</a>")]
    [InlineData("[*t*](u)", "<a href=\"u\"><em>t</em></a>")]
    [InlineData("[t](<a b>)", "<a href=\"a%20b\">t</a>")]
    [InlineData("[t](http://x.com/日本?q=%20)", "<a href=\"http://x.com/%E6%97%A5%E6%9C%AC?q=%20\">t</a>")]
    [InlineData("[t](a(b)c)", "<a href=\"a(b)c\">t</a>")]
    [InlineData("[t]", "[t]")]
    public void Links(string md, string expected) => Assert.Equal(expected, Inline(md));

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](JavaScript:alert(1))")]
    [InlineData("[x](vbscript:msgbox)")]
    [InlineData("[x](file:///C:/Windows)")]
    [InlineData("<javascript:alert(1)>")]
    public void DangerousLinks_StayAsText(string md) => Assert.Equal(md, Inline(md));

    [Theory]
    [InlineData("<https://a.b/c>", "<a href=\"https://a.b/c\">https://a.b/c</a>")]
    [InlineData("<me@x.org>", "<a href=\"mailto:me@x.org\">me@x.org</a>")]
    [InlineData("<not a link>", "<not a link>")]
    public void Autolinks(string md, string expected) => Assert.Equal(expected, Inline(md));

    // ---- ブロック ---------------------------------------------------------------------

    [Theory]
    [InlineData("# H1\n## H2 ##\n###### H6", "<h1>H1</h1><h2>H2</h2><h6>H6</h6>")]
    [InlineData("####### seven", "<p>####### seven</p>")]
    [InlineData("#nospace", "<p>#nospace</p>")]
    // 閉じの # は「空白の後ろから行末まで # だけ」のときに限る。それ以外の # は見出しの中身。
    [InlineData("# C# 入門", "<h1>C# 入門</h1>")]
    [InlineData("# foo#", "<h1>foo#</h1>")]
    [InlineData("### a ### b", "<h3>a ### b</h3>")]
    [InlineData("## a ##   ", "<h2>a</h2>")]
    [InlineData("# #", "<h1></h1>")]
    [InlineData("Title\n===\nSub\n---", "<h1>Title</h1><h2>Sub</h2>")]
    public void Headings(string md, string expected) => Assert.Equal(expected, Html(md));

    [Theory]
    [InlineData("```cs\nvar x;\n```\nafter", "<pre>var x;\n</pre><p>after</p>")]
    [InlineData("~~~\na\n\nb", "<pre>a\n\nb\n</pre>")]            // 閉じが無ければ末尾まで
    [InlineData("````\n```\n````", "<pre>```\n</pre>")]           // 開きより短い柵では閉じない
    [InlineData("    code\n\n    more\n\n", "<pre>code\n\nmore\n</pre>")]
    public void CodeBlocks(string md, string expected) => Assert.Equal(expected, Html(md));

    [Theory]
    [InlineData("***", "<hr>")]
    [InlineData("- - -", "<hr>")]
    [InlineData("___", "<hr>")]
    [InlineData("**", "<p>**</p>")]
    public void ThematicBreaks(string md, string expected) => Assert.Equal(expected, Html(md));

    [Fact]
    public void Quote_JoinsLinesAndNests()
        => Assert.Equal("<blockquote><p>a\nb</p><blockquote><p>c</p></blockquote></blockquote>",
            Html("> a\n> b\n>\n> > c"));

    [Theory]
    [InlineData("- a\n- b", "<ul><li><p>a</p></li><li><p>b</p></li></ul>")]
    [InlineData("3. x\n4. y", "<ol start=\"3\"><li><p>x</p></li><li><p>y</p></li></ol>")]
    [InlineData("- a\n1. b", "<ul><li><p>a</p></li></ul><ol><li><p>b</p></li></ol>")]
    [InlineData("- a\n  - b", "<ul><li><p>a</p><ul><li><p>b</p></li></ul></li></ul>")]
    [InlineData("- a\n\n- b", "<ul><li><p>a</p></li><li><p>b</p></li></ul>")]
    [InlineData("- a\n\n  more", "<ul><li><p>a</p><p>more</p></li></ul>")]
    [InlineData("- a\nlazy", "<ul><li><p>a\nlazy</p></li></ul>")]
    [InlineData("*text", "<p>*text</p>")]
    public void Lists(string md, string expected) => Assert.Equal(expected, Html(md));

    [Theory]
    [InlineData("para\n# h", "<p>para</p><h1>h</h1>")]
    [InlineData("para\n- item", "<p>para</p><ul><li><p>item</p></li></ul>")]
    [InlineData("para\n> q", "<p>para</p><blockquote><p>q</p></blockquote>")]
    [InlineData("para\n```\nc\n```", "<p>para</p><pre>c\n</pre>")]
    [InlineData("para\n***", "<p>para</p><hr>")]
    public void BlocksInterruptParagraphs(string md, string expected) => Assert.Equal(expected, Html(md));

    [Fact]
    public void Table_AlignsAndAutocompletesShortRows()
        => Assert.Equal(
            "<table><tr><th align=\"left\">a</th><th align=\"right\">b</th><th align=\"center\">c</th></tr>"
            + "<tr><td align=\"left\">1</td><td align=\"right\">2</td><td align=\"center\">3</td></tr>"
            + "<tr><td align=\"left\">4</td><td align=\"right\"></td><td align=\"center\"></td></tr></table>",
            Html("| a | b | c |\n|:--|--:|:-:|\n| 1 | 2 | 3 |\n| 4 |"));

    [Fact]
    public void Table_EscapedPipeStaysInCell()
        => Assert.Equal("<table><tr><th>a | b</th></tr></table>", Html("| a \\| b |\n|---|"));

    [Fact]
    public void Table_ColumnCountMismatch_IsAParagraph()
        => Assert.Equal("<p>| a | b |\n|---|</p>", Html("| a | b |\n|---|"));

    [Fact]
    public void Table_InterruptsParagraphAndEndsAtBlankLine()
        => Assert.Equal("<p>text</p><table><tr><th>a</th></tr><tr><td>1</td></tr></table><p>after</p>",
            Html("text\n| a |\n|---|\n| 1 |\n\nafter"));

    [Fact]
    public void CrLf_IsTreatedAsLf() => Assert.Equal("<h1>a</h1><p>b\nc</p>", Html("# a\r\nb\r\nc"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\n \n")]
    public void Blank_HasNoBlocks(string? md) => Assert.Empty(MarkdownParser.Parse(md));
}
