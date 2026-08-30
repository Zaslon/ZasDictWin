using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;

namespace ZasDictWin.Services;

/// <summary>
/// Markdown を WPF の FlowDocument に描画する。MarkdownParser.Parse が作る AST
/// （MdParagraph / MdHeading / …）だけを見て、Theme.xaml のブラシと文字サイズ倍率を適用する。
/// </summary>
public static class Markdown
{
    private static readonly FontFamily CodeFont = new("Consolas, Yu Gothic UI, Meiryo");

    // 見出しレベル 1〜6 のサイズ係数（基準 14px に掛ける）。
    private static readonly double[] HeadingFactor = { 1.7, 1.45, 1.25, 1.1, 1.0, 0.95 };

    public static FlowDocument ToFlowDocument(string? source, double scale = 1.0)
    {
        if (scale <= 0) scale = 1.0;
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            ColumnWidth = double.PositiveInfinity,
            Background = Brushes.Transparent,
            FontSize = S(14, scale),
        };
        foreach (var block in RenderBlocks(MarkdownParser.Parse(source), 0.0, scale))
            doc.Blocks.Add(block);
        return doc;
    }

    // ---- ブロック ----------------------------------------------------------------

    private static List<Block> RenderBlocks(List<MdNode> blocks, double indent, double scale)
    {
        var result = new List<Block>();
        foreach (var node in blocks)
        {
            switch (node)
            {
                case MdParagraph p:
                {
                    var para = new Paragraph { Margin = new Thickness(indent, 0, 0, S(8, scale)) };
                    foreach (var inline in BuildInlines(p.Inlines, scale)) para.Inlines.Add(inline);
                    result.Add(para);
                    break;
                }
                case MdHeading h:
                {
                    var factor = HeadingFactor[Math.Clamp(h.Level - 1, 0, HeadingFactor.Length - 1)];
                    var para = new Paragraph
                    {
                        FontWeight = FontWeights.Bold,
                        FontSize = S(14 * factor, scale),
                        Margin = new Thickness(indent, S(h.Level <= 2 ? 16 : 12, scale), 0, S(6, scale)),
                    };
                    foreach (var inline in BuildInlines(h.Inlines, scale)) para.Inlines.Add(inline);
                    result.Add(para);
                    break;
                }
                case MdCodeBlock c:
                {
                    var para = new Paragraph
                    {
                        FontFamily = CodeFont,
                        FontSize = S(13, scale),
                        Background = Res("Raised", 0x232A38),
                        BorderBrush = Res("Line", 0x333C4E),
                        BorderThickness = new Thickness(S(1, scale)),
                        Padding = new Thickness(S(10, scale), S(6, scale), S(10, scale), S(6, scale)),
                        Margin = new Thickness(indent, 0, 0, S(8, scale)),
                    };
                    var lines = c.Text.TrimEnd('\n').Split('\n');
                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (i > 0) para.Inlines.Add(new LineBreak());
                        para.Inlines.Add(new Run(lines[i]));
                    }
                    result.Add(para);
                    break;
                }
                case MdQuote q:
                {
                    var children = RenderBlocks(q.Blocks, indent + S(14, scale), scale);
                    var border = Res("Line", 0x333C4E);
                    foreach (var child in children)
                    {
                        if (child is Paragraph cp)
                        {
                            cp.BorderBrush = border;
                            cp.BorderThickness = new Thickness(S(3, scale), 0, 0, 0);
                        }
                        result.Add(child);
                    }
                    break;
                }
                case MdList l:
                {
                    for (var i = 0; i < l.Items.Count; i++)
                    {
                        var marker = l.Ordered ? $"{l.Start + i}. " : "• ";
                        var itemBlocks = RenderBlocks(l.Items[i].Blocks, indent + S(18, scale), scale);
                        if (!PrependMarker(itemBlocks, marker, indent, scale))
                        {
                            var label = new Paragraph(new Run(marker) { Foreground = Res("Muted", 0x94A0B8) })
                            {
                                Margin = new Thickness(indent, 0, 0, S(2, scale)),
                            };
                            result.Add(label);
                        }
                        result.AddRange(itemBlocks);
                    }
                    break;
                }
                case MdThematicBreak:
                {
                    var para = new Paragraph(new Run(""))
                    {
                        BorderBrush = Res("Line", 0x333C4E),
                        BorderThickness = new Thickness(0, S(1, scale), 0, 0),
                        FontSize = S(1, scale),
                        Margin = new Thickness(indent, S(12, scale), 0, S(12, scale)),
                    };
                    result.Add(para);
                    break;
                }
                case MdTable t:
                    result.Add(BuildTable(t, indent, scale));
                    break;
            }
        }
        return result;
    }

    /// <summary>項目の先頭段落にマーカー（• や 3.）を差し込む。戻り値は成功可否。</summary>
    private static bool PrependMarker(List<Block> itemBlocks, string marker, double indent, double scale)
    {
        if (itemBlocks.Count == 0) return false;
        if (itemBlocks[0] is Paragraph first)
        {
            // InlineCollection には Insert が無いので、マーカーを先頭に置いた段落を
            // 作り直し、既存インラインを移し替える。
            var repl = new Paragraph(new Run(marker) { Foreground = Res("Muted", 0x94A0B8) });
            CopyParagraphProps(first, repl);
            while (first.Inlines.FirstInline is { } head)
            {
                first.Inlines.Remove(head);
                repl.Inlines.Add(head);
            }
            itemBlocks[0] = repl;
            return true;
        }
        // 先頭が段落以外（入れ子リスト等）のときはマーカー専用の行を置く。
        var label = new Paragraph(new Run(marker) { Foreground = Res("Muted", 0x94A0B8) })
        {
            Margin = new Thickness(indent, 0, 0, S(2, scale)),
        };
        itemBlocks.Insert(0, label);
        return true;
    }

    private static void CopyParagraphProps(Paragraph src, Paragraph dst)
    {
        dst.Margin = src.Margin;
        dst.BorderBrush = src.BorderBrush;
        dst.BorderThickness = src.BorderThickness;
        dst.Background = src.Background;
        dst.Padding = src.Padding;
    }

    /// <summary>GFM 表を WPF Table に変換する。枠線は GitHub 風（外枠＋列区切り＋ヘッダー下線）、
    /// 列の整列は区切り行のコロンの位置に従う。</summary>
    private static Block BuildTable(MdTable t, double indent, double scale)
    {
        var line = Res("Line", 0x333C4E);
        double bw = Math.Max(1.0, S(1, scale));
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(indent, S(4, scale), 0, S(10, scale)),
            BorderBrush = line,
            BorderThickness = new Thickness(bw),
        };

        TableRow MakeRow(MdTableRow row, bool header)
        {
            var tr = new TableRow();
            int last = row.Cells.Count - 1;
            for (int c = 0; c <= last; c++)
            {
                var para = new Paragraph { Margin = new Thickness(0) };
                foreach (var inline in BuildInlines(row.Cells[c], scale)) para.Inlines.Add(inline);
                if (para.Inlines.Count == 0) para.Inlines.Add(new Run(" "));
                if (header) para.FontWeight = FontWeights.Bold;
                var align = c < t.Aligns.Count ? t.Aligns[c] : MdAlignKind.None;
                para.TextAlignment = align switch
                {
                    MdAlignKind.Center => TextAlignment.Center,
                    MdAlignKind.Right => TextAlignment.Right,
                    _ => TextAlignment.Left,
                };
                var cell = new TableCell(para)
                {
                    Padding = new Thickness(S(9, scale), S(4, scale), S(9, scale), S(5, scale)),
                    BorderBrush = line,
                    BorderThickness = new Thickness(0, 0, c < last ? bw : 0, header ? bw : 0),
                };
                tr.Cells.Add(cell);
            }
            return tr;
        }

        var headGroup = new TableRowGroup();
        headGroup.Rows.Add(MakeRow(t.Head, true));
        table.RowGroups.Add(headGroup);
        if (t.Body.Count > 0)
        {
            var bodyGroup = new TableRowGroup();
            foreach (var row in t.Body) bodyGroup.Rows.Add(MakeRow(row, false));
            table.RowGroups.Add(bodyGroup);
        }
        return table;
    }

    // ---- インライン --------------------------------------------------------------

    private static IEnumerable<Inline> BuildInlines(List<MdInline> inlines, double scale)
    {
        foreach (var node in inlines)
        {
            switch (node)
            {
                case MdText t:
                    yield return new Run(t.Value);
                    break;
                case MdSoftBreak:
                    // HTML と同じく改行は空白扱い（CJK の行頭揃えはブラウザと同挙動）。
                    yield return new Run(" ");
                    break;
                case MdHardBreak:
                    yield return new LineBreak();
                    break;
                case MdCodeSpan c:
                    yield return new Run(c.Value)
                    {
                        FontFamily = CodeFont,
                        Background = Res("Raised", 0x232A38),
                    };
                    break;
                case MdStrong s:
                    yield return WrapInline(new Bold(), s.Children, scale);
                    break;
                case MdEm e:
                    yield return WrapInline(new Italic(), e.Children, scale);
                    break;
                case MdStrike st:
                {
                    var span = new Span { TextDecorations = TextDecorations.Strikethrough };
                    yield return WrapInline(span, st.Children, scale);
                    break;
                }
                case MdLink l:
                {
                    var link = new Hyperlink { Foreground = Res("Accent", 0xA78BFA) };
                    if (TryUri(l.Url, out var uri)) link.NavigateUri = uri;
                    link.RequestNavigate += OnRequestNavigate;
                    WrapInline(link, l.Children, scale);
                    yield return link;
                    break;
                }
            }
        }
    }

    private static Inline WrapInline(Span span, List<MdInline> children, double scale)
    {
        foreach (var child in BuildInlines(children, scale)) span.Inlines.Add(child);
        return span;
    }

    private static bool TryUri(string url, out Uri? uri)
    {
        uri = null;
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != "mailto"))
                uri = null;
        }
        catch (Exception)
        {
            uri = null;
        }
        return uri is not null;
    }

    private static void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            if (e.Uri is not null)
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // 起動できない URL（既定ブラウザ未設定等）は握りつぶす。
        }
        e.Handled = true;
    }

    // ---- 補助 --------------------------------------------------------------------

    private static double S(double baseSize, double scale) => baseSize * scale;

    /// <summary>Theme.xaml のブラシを解決できなかった場合のフォールバックは Theme.xaml と同じ色。</summary>
    private static Brush Res(string key, uint fallbackRgb)
    {
        if (Application.Current?.TryFindResource(key) is Brush found) return found;
        var fallback = new SolidColorBrush(Color.FromRgb((byte)(fallbackRgb >> 16), (byte)(fallbackRgb >> 8), (byte)fallbackRgb));
        fallback.Freeze();
        return fallback;
    }
}