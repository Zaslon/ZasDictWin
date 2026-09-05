using System.Text;
using System.Text.RegularExpressions;

namespace ZasDictWin.Services;

// ============================================================================
// Markdown AST（内部用）。Markdown.ToFlowDocument がこれだけを見て描画する。
// ============================================================================
internal abstract class MdNode { }

internal sealed class MdParagraph : MdNode
{
    public List<MdInline> Inlines = new();
}

internal sealed class MdHeading : MdNode
{
    public int Level;
    public List<MdInline> Inlines = new();
}

internal sealed class MdCodeBlock : MdNode
{
    public string Text = "";
}

internal sealed class MdQuote : MdNode
{
    public List<MdNode> Blocks = new();
}

internal sealed class MdList : MdNode
{
    public bool Ordered;
    public int Start = 1;
    public List<MdListItem> Items = new();
}

internal sealed class MdListItem : MdNode
{
    public List<MdNode> Blocks = new();
}

internal sealed class MdThematicBreak : MdNode { }

internal enum MdAlignKind { None, Left, Center, Right }

/// <summary>GFM 表の 1 行。Cells はインライン列のまま保持（セル単位で個別にパース済み）。</summary>
internal sealed class MdTableRow
{
    public List<List<MdInline>> Cells = new();
}

/// <summary>GFM パイプ表。Head は常に 1 行、Body は 0 行以上。Aligns は列数と同じ長さ。</summary>
internal sealed class MdTable : MdNode
{
    public List<MdAlignKind> Aligns = new();
    public MdTableRow Head = new();
    public List<MdTableRow> Body = new();
}

internal abstract class MdInline { }

internal sealed class MdText : MdInline
{
    public string Value = "";
}

internal sealed class MdSoftBreak : MdInline { }

internal sealed class MdHardBreak : MdInline { }

internal sealed class MdCodeSpan : MdInline
{
    public string Value = "";
}

internal sealed class MdLink : MdInline
{
    public string Url = "";
    public string? Title;
    public List<MdInline> Children = new();
}

internal sealed class MdEm : MdInline
{
    public List<MdInline> Children = new();
}

internal sealed class MdStrong : MdInline
{
    public List<MdInline> Children = new();
}

internal sealed class MdStrike : MdInline
{
    public List<MdInline> Children = new();
}

/// <summary>
/// 実用的な CommonMark サブセットのパーサー。インライン（強調・コード・リンク等）は
/// markdown-it と同一アルゴリズム（delimiter stack / processDelimiters）に、
/// markdown-it-cjk-friendly の scanDelims 上書きを組み込んだ移植版で、
/// CJK 文字の隣でも <c>*</c> による強調が正しく効く。
/// </summary>
internal static class MarkdownParser
{
    public static List<MdNode> Parse(string? text) => MdBlocks.Parse(text ?? "");
}
// ============================================================================
// ブロックレベルパーサー
// ============================================================================
internal static class MdBlocks
{
    private static readonly Regex SetextRe = new(@"^ {0,3}(=+|-+) *$", RegexOptions.Compiled);
    private static readonly Regex DelimCellRe = new(@"^:?-+:?$", RegexOptions.Compiled);

    public static List<MdNode> Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocks = new List<MdNode>();
        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (IsBlank(line)) { i++; continue; }

            // GFM 表（markdown-it v14 ではブロックルールの中で最初に試される。段落を中断できる）。
            if (TryTable(lines, i, out var tableNode, out int tableNext))
            {
                blocks.Add(tableNode!);
                i = tableNext;
                continue;
            }

            // 囲みコードブロック（``` / ~~~）。閉じフェンスが無い場合は末尾まで。
            var fence = TryFenceOpen(line);
            if (fence is not null)
            {
                int j = i + 1;
                while (j < lines.Length && !IsFenceClose(lines[j], fence.Value)) j++;
                var content = new StringBuilder();
                // markdown-it と同じく各行は改行を含む（末尾 \n も内容に含める）。
                for (int k = i + 1; k < j; k++) content.Append(lines[k]).Append('\n');
                blocks.Add(new MdCodeBlock { Text = content.ToString() });
                i = j < lines.Length ? j + 1 : lines.Length;
                continue;
            }

            // ATX 見出し
            int atxLevel;
            if (TryAtx(line, out atxLevel))
            {
                blocks.Add(new MdHeading { Level = atxLevel, Inlines = MdInlines.Parse(AtxContent(line)) });
                i++;
                continue;
            }

            // 水平線（リストより先に判定する：*** は HR）
            if (IsThematicBreak(line))
            {
                blocks.Add(new MdThematicBreak());
                i++;
                continue;
            }

            // 引用
            int qInd = IndentOf(line);
            if (line.Length > 0 && qInd <= 3 && line[qInd] == '>')
            {
                var quote = new List<string>();
                while (i < lines.Length)
                {
                    var l = lines[i];
                    if (IsBlank(l)) break;
                    int ind = IndentOf(l);
                    if (ind > 3 || l[ind] != '>') break;
                    string rest = l[(ind + 1)..];
                    if (rest.StartsWith(" ")) rest = rest[1..];
                    quote.Add(rest);
                    i++;
                }
                blocks.Add(new MdQuote { Blocks = Parse(string.Join("\n", quote)) });
                continue;
            }

            // リスト
            if (TryListItem(line, out var liInd, out var liOrd, out var liNum, out var liCol))
            {
                var list = new MdList { Ordered = liOrd, Start = liNum };
                while (i < lines.Length && !IsBlank(lines[i])
                       && TryListItem(lines[i], out var ind2, out var ord2, out var num2, out var col2)
                       && ord2 == liOrd && ind2 == liInd)
                {
                    i++;
                    // 遅延継続行はブロックのインデントを持たないため、デデントしてはいけない（markdown-it 準拠）。
                    var itemLines = new List<(string Line, bool Lazy)> { (lines[i - 1], false) };
                    while (i < lines.Length)
                    {
                        var l = lines[i];
                        if (IsBlank(l))
                        {
                            // 空行の次が本文列に達していればこの項目の続き（緩いリスト）として吸収。
                            int k = i;
                            while (k < lines.Length && IsBlank(lines[k])) k++;
                            if (k < lines.Length && IndentOf(lines[k]) >= col2)
                            {
                                for (; i < k; i++) itemLines.Add(("", false));
                                continue;
                            }
                            break;
                        }
                        if (IndentOf(l) >= col2) { itemLines.Add((l, false)); i++; continue; }
                        // 遅延段落継続：マーカーやブロック開始でない素のテキスト行のみ吸収。
                        bool blocked = TryListItem(l, out _, out _, out _, out _)
                            || TryFenceOpen(l) is not null || TryAtx(l, out _) || IsThematicBreak(l);
                        if (!blocked && l[IndentOf(l)] != '>' && !IsBlank(itemLines[^1].Line))
                        {
                            itemLines.Add((l, true)); i++; continue;
                        }
                        break;
                    }
                    var dedented = itemLines.Select(x => x.Lazy ? x.Line : (x.Line.Length > col2 ? x.Line[col2..] : "")).ToList();
                    list.Items.Add(new MdListItem { Blocks = Parse(string.Join("\n", dedented)) });
                    // 兄弟項目の間の空行を飛ばす（緩いリストでも項目を続けられるようにする）。
                    int m3 = i;
                    while (m3 < lines.Length && IsBlank(lines[m3])) m3++;
                    if (m3 < lines.Length && IndentOf(lines[m3]) == liInd
                        && TryListItem(lines[m3], out _, out var ord3, out _, out _) && ord3 == liOrd)
                        i = m3;
                }
                blocks.Add(list);
                continue;
            }

            // 字下げコードブロック（4 スペース以上）
            if (IndentOf(line) >= 4)
            {
                var codeLines = new List<string>();
                while (i < lines.Length)
                {
                    var l = lines[i];
                    if (IsBlank(l)) { codeLines.Add(""); i++; continue; }
                    if (IndentOf(l) < 4) break;
                    codeLines.Add(l[4..]);
                    i++;
                }
                while (codeLines.Count > 0 && codeLines[^1].Length == 0) codeLines.RemoveAt(codeLines.Count - 1);
                blocks.Add(new MdCodeBlock { Text = string.Join("\n", codeLines) + "\n" });
                continue;
            }

            // 段落（直後に setext アンダースコアが続けば見出しになる）
            var para = new List<string> { line };
            i++;
            bool setextDone = false;
            while (i < lines.Length)
            {
                var l = lines[i];
                if (IsBlank(l)) break;
                if (SetextRe.IsMatch(l))
                {
                    int level = l.TrimStart()[0] == '=' ? 1 : 2;
                    blocks.Add(new MdHeading { Level = level, Inlines = MdInlines.Parse(string.Join("\n", para)) });
                    i++;
                    setextDone = true;
                    break;
                }
                int ind = IndentOf(l);
                if (ind <= 3 && l[ind] == '>') break;
                if (TryFenceOpen(l) is not null) break;
                if (TryAtx(l, out _)) break;
                if (IsThematicBreak(l)) break;
                if (TryListItem(l, out _, out _, out _, out _)) break;
                // GFM 表は段落を中断する（markdown-it の paragraph terminator rules と同じ）。
                if (IsTableStart(lines, i)) break;
                para.Add(l);
                i++;
            }
            if (!setextDone)
                blocks.Add(new MdParagraph { Inlines = MdInlines.Parse(string.Join("\n", para)) });
        }
        return blocks;
    }
    private static bool IsBlank(string s) => s.Trim().Length == 0;

    private static int IndentOf(string s)
    {
        int n = 0;
        while (n < s.Length && s[n] == ' ') n++;
        return n;
    }

    private static (char Ch, int Len)? TryFenceOpen(string line)
    {
        int ind = IndentOf(line);
        if (ind > 3 || ind >= line.Length) return null;
        char ch = line[ind];
        if (ch != '`' && ch != '~') return null;
        int len = 0;
        while (ind + len < line.Length && line[ind + len] == ch) len++;
        if (len < 3) return null;
        if (ch == '`' && line[(ind + len)..].Contains('`')) return null; // 情報文字列に ` が含まれる囲みは不可
        return (ch, len);
    }

    private static bool IsFenceClose(string line, (char Ch, int Len) fence)
    {
        int ind = IndentOf(line);
        if (ind > 3 || ind >= line.Length) return false;
        if (line[ind] != fence.Ch) return false;
        int len = 0;
        while (ind + len < line.Length && line[ind + len] == fence.Ch) len++;
        return len >= fence.Len && line[(ind + len)..].Trim().Length == 0;
    }

    private static bool TryAtx(string line, out int level)
    {
        level = 0;
        int ind = IndentOf(line);
        if (ind > 3 || ind >= line.Length) return false;
        while (ind + level < line.Length && line[ind + level] == '#' && level < 6) level++;
        if (level == 0) return false;
        if (ind + level < line.Length && line[ind + level] != ' ') return false; // # の後は空白か行末
        return true;
    }

    private static string AtxContent(string line)
    {
        int ind = IndentOf(line);
        int level = 0;
        while (ind + level < line.Length && line[ind + level] == '#') level++;
        var sb = new StringBuilder();
        for (int k = ind + level; k < line.Length; k++)
        {
            if (line[k] == '#') break; // 閉じシーケンス（行末の # の連続）まで
            sb.Append(line[k]);
        }
        return sb.ToString().Trim();
    }

    private static bool IsThematicBreak(string line)
    {
        if (IndentOf(line) > 3) return false;
        char ch = '\0';
        int count = 0;
        foreach (var c in line.TrimEnd())
        {
            if (c == ' ') continue;
            if (ch == '\0') ch = c;
            else if (c != ch) return false;
            count++;
        }
        return (ch == '-' || ch == '*' || ch == '_') && count >= 3;
    }

    private static bool IsDelimChar(char c) => c == '|' || c == '-' || c == ':' || c == ' ' || c == '\t';

    /// <summary>markdown-it の escapedSplit：バックスラッシュでエスケープされたパイプを区切りとして扱わず、
    /// エスケープ記号自身を除いてセルへ復元する。</summary>
    private static List<string> EscapedSplit(string str)
    {
        var result = new List<string>();
        int max = str.Length, pos = 0, lastPos = 0;
        string current = "";
        bool isEscaped = false;
        char ch = max > 0 ? str[0] : '\0';
        while (pos < max)
        {
            if (ch == '|')
            {
                if (!isEscaped)
                {
                    result.Add(current + str.Substring(lastPos, pos - lastPos));
                    current = "";
                    lastPos = pos + 1;
                }
                else
                {
                    int len = Math.Max(0, pos - 1 - lastPos);
                    current += str.Substring(lastPos, len);
                    lastPos = pos;
                }
            }
            isEscaped = ch == '\\';
            pos++;
            ch = pos < max ? str[pos] : '\0';
        }
        result.Add(current + str.Substring(lastPos));
        return result;
    }

    private static bool IsTableStart(string[] lines, int start) => TryTable(lines, start, out _, out _);

    /// <summary>GFM 表の判定と構築（markdown-it v14 の table ルールに準拠、blkIndent=0 前提。
    /// 引用やリストの内側は再帰 Parse 経由で到達するため、それで同じ結果になる）。</summary>
    private static bool TryTable(string[] lines, int start, out MdTable? table, out int next)
    {
        table = null;
        next = start;
        // ヘッダー行 + 区切り行が存在すること（startLine + 2 > endLine なら不成立）。
        if (start + 2 > lines.Length) return false;

        var dline = lines[start + 1];
        if (IsBlank(dline)) return false;
        if (IndentOf(dline) >= 4) return false;
        string dt = dline.Trim();
        // 先頭 2 文字は | - : のいずれか（2 文字目は空白可）。1 文字目 '-' + 空白 は区切りにならない。
        if (dt.Length < 2) return false;
        char first = dt[0], second = dt[1];
        if (first != '|' && first != '-' && first != ':') return false;
        if (second != '|' && second != '-' && second != ':' && second != ' ' && second != '\t') return false;
        if (first == '-' && (second == ' ' || second == '\t')) return false;
        for (int p = 2; p < dt.Length; p++) if (!IsDelimChar(dt[p])) return false;

        // 区切り行から列数と整列を作る。先頭 / 末尾の空セル（境界パイプ）は列に数えない。
        var aligns = new List<MdAlignKind>();
        var dcols = dt.Split('|');
        for (int c = 0; c < dcols.Length; c++)
        {
            string t = dcols[c].Trim();
            if (t.Length == 0)
            {
                if (c == 0 || c == dcols.Length - 1) continue;
                return false;
            }
            if (!DelimCellRe.IsMatch(t)) return false;
            bool left = t[0] == ':', right = t[^1] == ':';
            aligns.Add(right ? (left ? MdAlignKind.Center : MdAlignKind.Right)
                             : (left ? MdAlignKind.Left : MdAlignKind.None));
        }

        var hraw = lines[start];
        if (IndentOf(hraw) >= 4) return false;
        string htext = hraw.Trim();
        if (htext.IndexOf('|') < 0) return false;
        var hcols = EscapedSplit(htext);
        if (hcols.Count > 0 && hcols[0].Length == 0) hcols.RemoveAt(0);
        if (hcols.Count > 0 && hcols[^1].Length == 0) hcols.RemoveAt(hcols.Count - 1);
        int columnCount = hcols.Count;
        if (columnCount == 0 || columnCount != aligns.Count) return false;

        var tbl = new MdTable { Aligns = aligns };
        var headRow = new MdTableRow();
        foreach (var hc in hcols) headRow.Cells.Add(MdInlines.Parse(hc.Trim()));
        tbl.Head = headRow;

        // 本文行。空行 / インデント 4 以上 / 引用系ターミネーター（hr・fence・引用・リスト・見出し）で終了。
        int autocompleted = 0;
        int nl;
        for (nl = start + 2; nl < lines.Length; nl++)
        {
            var l = lines[nl];
            if (IsBlank(l)) break;
            int ind = IndentOf(l);
            bool blocked = TryListItem(l, out _, out _, out _, out _)
                || TryFenceOpen(l) is not null || TryAtx(l, out _) || IsThematicBreak(l)
                || (ind <= 3 && l[ind] == '>');
            if (blocked) break;
            string lt = l.Trim();
            if (lt.Length == 0) break;
            if (ind >= 4) break;
            var bcols = EscapedSplit(lt);
            if (bcols.Count > 0 && bcols[0].Length == 0) bcols.RemoveAt(0);
            if (bcols.Count > 0 && bcols[^1].Length == 0) bcols.RemoveAt(bcols.Count - 1);
            autocompleted += columnCount - bcols.Count;
            if (autocompleted > 65536) break;
            var row = new MdTableRow();
            for (int c = 0; c < columnCount; c++)
                row.Cells.Add(MdInlines.Parse(c < bcols.Count ? bcols[c].Trim() : ""));
            tbl.Body.Add(row);
        }

        table = tbl;
        next = nl;
        return true;
    }

    private static bool TryListItem(string line, out int indent, out bool ordered, out int number, out int contentCol)
    {
        indent = 0; ordered = false; number = 1; contentCol = -1;
        while (indent < line.Length && line[indent] == ' ') indent++;
        if (indent > 3 || indent >= line.Length) return false;
        char c = line[indent];
        int markerEnd;
        if (c == '-' || c == '+' || c == '*')
        {
            markerEnd = indent + 1;
        }
        else if (c >= '0' && c <= '9')
        {
            int j = indent;
            while (j < line.Length && line[j] >= '0' && line[j] <= '9') j++;
            if (j - indent > 9) return false;
            if (j >= line.Length || (line[j] != '.' && line[j] != ')')) return false;
            ordered = true;
            number = int.Parse(line[indent..j]);
            markerEnd = j + 1;
        }
        else return false;
        if (markerEnd < line.Length && line[markerEnd] != ' ') return false; // マーカー後は空白必須（*字 は項目でない）
        contentCol = markerEnd + 1;
        return true;
    }
}
// ============================================================================
// インラインパーサー（markdown-it の rules_inline を移植、scanDelims は CJK 対応版）
// ============================================================================
internal static class MdInlines
{
    private sealed class State
    {
        public string Src = "";
        public int Pos, PosMax;
        public List<Tok> Tokens = new();
        public List<Delim> Delims = new();
        public StringBuilder Pending = new();
        public Dictionary<int, int>? Backticks;
        public int LinkLevel;
    }

    private sealed class Tok
    {
        public string Type = "text";
        public string Content = "";
        public string? Url, Title;
    }

    private sealed class Delim
    {
        public char Marker;
        public int Length;      // 連続マーカー数（~~ は 0）
        public int Token;       // Tokens 内のインデックス
        public int End = -1;    // 対応する closer の Delims インデックス
        public bool Open, Close;
    }

    private static readonly HashSet<char> TextTerminators = new()
    {
        '\n', '!', '#', '$', '%', '&', '*', '+', '-', ':', '<', '=', '>', '@', '[', '\\',
        ']', '^', '_', '`', '{', '}', '~'
    };

    // \G は Match(src, pos) の開始位置に固定される（markdown-it の sticky フラグ相当）。
    private static readonly Regex DigitalEntityRe = new(@"\G&#((?:x[0-9a-fA-F]{1,6}|[0-9]{1,7}));", RegexOptions.Compiled);
    private static readonly Regex AutolinkUrlRe = new(@"^[a-zA-Z][a-zA-Z0-9+.-]{1,31}:[^<>\x00-\x20]*$", RegexOptions.Compiled);
    private static readonly Regex AutolinkEmailRe = new(
        @"^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$",
        RegexOptions.Compiled);
    private static readonly Regex DangerousProtocolRe = new(@"^(javascript|vbscript|file)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, string> NamedEntities = new(StringComparer.Ordinal)
    {
        ["amp"] = "&", ["lt"] = "<", ["gt"] = ">", ["quot"] = "\"", ["apos"] = "'",
        ["nbsp"] = "\u00A0", ["copy"] = "\u00A9", ["reg"] = "\u00AE", ["trade"] = "\u2122",
        ["hellip"] = "\u2026", ["mdash"] = "\u2014", ["ndash"] = "\u2013",
        ["lsquo"] = "\u2018", ["rsquo"] = "\u2019", ["ldquo"] = "\u201C", ["rdquo"] = "\u201D",
        ["dagger"] = "\u2020", ["Dagger"] = "\u2021", ["bull"] = "\u2022",
        ["permil"] = "\u2030", ["laquo"] = "\u00AB", ["raquo"] = "\u00BB",
        ["middot"] = "\u00B7", ["deg"] = "\u00B0", ["plusmn"] = "\u00B1",
        ["times"] = "\u00D7", ["divide"] = "\u00F7",
        ["larr"] = "\u2190", ["uarr"] = "\u2191", ["rarr"] = "\u2192", ["darr"] = "\u2193",
        ["harr"] = "\u2194", ["crarr"] = "\u21B5",
        ["spades"] = "\u2660", ["clubs"] = "\u2663", ["hearts"] = "\u2665", ["diams"] = "\u2666",
    };

    public static List<MdInline> Parse(string text)
    {
        var st = new State { Src = text, PosMax = text.Length };
        Tokenize(st);
        ProcessDelimiters(st.Delims);
        EmphasisPostProcess(st);
        StrikePostProcess(st);
        return BuildTree(st.Tokens);
    }

    private static void Flush(State st)
    {
        if (st.Pending.Length > 0)
        {
            st.Tokens.Add(new Tok { Type = "text", Content = st.Pending.ToString() });
            st.Pending.Clear();
        }
    }

    private static Tok Push(State st, string type)
    {
        Flush(st);
        var tok = new Tok { Type = type };
        st.Tokens.Add(tok);
        return tok;
    }

    private static void Tokenize(State st)
    {
        while (st.Pos < st.PosMax)
        {
            int oldPos = st.Pos;
            if (RuleText(st) || RuleNewline(st) || RuleEscape(st) || RuleBacktick(st)
                || RuleStrike(st) || RuleEmphasis(st) || RuleLink(st) || RuleAutolink(st) || RuleEntity(st))
                continue;
            st.Pending.Append(st.Src[st.Pos]);
            st.Pos = oldPos + 1;
        }
        Flush(st);
    }

    private static bool RuleText(State st)
    {
        int pos = st.Pos;
        while (pos < st.PosMax && !TextTerminators.Contains(st.Src[pos])) pos++;
        if (pos == st.Pos) return false;
        st.Pending.Append(st.Src, st.Pos, pos - st.Pos);
        st.Pos = pos;
        return true;
    }

    private static bool RuleNewline(State st)
    {
        if (st.Src[st.Pos] != '\n') return false;
        int pmax = st.Pending.Length - 1;
        if (pmax >= 1 && st.Pending[pmax] == ' ' && st.Pending[pmax - 1] == ' ')
        {
            int ws = pmax - 1;
            while (ws >= 1 && st.Pending[ws - 1] == ' ') ws--;
            st.Pending.Length = ws;
            Push(st, "hardbreak");
        }
        else
        {
            Push(st, "softbreak");
        }
        st.Pos++;
        while (st.Pos < st.PosMax && IsMdWhiteSpace(CpAt(st.Src, st.Pos))) st.Pos++;
        return true;
    }

    private static bool RuleEscape(State st)
    {
        if (st.Src[st.Pos] != '\\') return false;
        int pos = st.Pos + 1;
        if (pos >= st.PosMax) return false;
        char ch = st.Src[pos];
        if (ch == '\n')
        {
            Push(st, "hardbreak");
            pos++;
            while (pos < st.PosMax && IsMdWhiteSpace(CpAt(st.Src, pos))) pos++;
            st.Pos = pos;
            return true;
        }
        if (ch == ' ')
        {
            var tok = Push(st, "text");
            tok.Content = "\\";
            st.Pos = pos;
            return true;
        }
        if (IsMdAsciiPunct(ch))
        {
            var tok = Push(st, "text");
            tok.Content = ch.ToString();
            st.Pos = pos + 1;
            return true;
        }
        return false;
    }

    private static bool RuleBacktick(State st)
    {
        int start = st.Pos;
        if (st.Src[start] != '`') return false;
        int pos = start + 1;
        while (pos < st.PosMax && st.Src[pos] == '`') pos++;
        int openLen = pos - start;

        if (st.Backticks is null) st.Backticks = BuildBacktickRuns(st.Src);
        if (st.Backticks.TryGetValue(openLen, out int lastRun) && lastRun >= pos)
        {
            int matchEnd = pos;
            while (true)
            {
                int matchStart = st.Src.IndexOf('`', matchEnd);
                if (matchStart < 0 || matchStart >= st.PosMax) break;
                matchEnd = matchStart + 1;
                while (matchEnd < st.PosMax && st.Src[matchEnd] == '`') matchEnd++;
                if (matchEnd > st.PosMax) break;
                if (matchEnd - matchStart == openLen)
                {
                    var content = st.Src[pos..matchStart].Replace("\n", " ");
                    if (content.Length >= 2 && content.StartsWith(" ") && content.EndsWith(" ") && content.Trim(' ').Length > 0)
                        content = content[1..^1];
                    var tok = Push(st, "code");
                    tok.Content = content;
                    st.Pos = matchEnd;
                    return true;
                }
            }
        }
        // 閉じが見つからない：開始シーケンスをリテラルとして扱う（markdown-it と同じ）
        st.Pending.Append(st.Src, start, openLen);
        st.Pos = pos;
        return true;
    }
    private static bool RuleStrike(State st)
    {
        int start = st.Pos;
        if (st.Src[start] != '~') return false;
        var scanned = ScanDelims(st, start, canSplitWord: true);
        int len = scanned.Length;
        if (len < 2) return false;
        if (len % 2 == 1)
        {
            var tok = Push(st, "text");
            tok.Content = "~";
            len--;
        }
        for (int i = 0; i < len; i += 2)
        {
            var tok = Push(st, "text");
            tok.Content = "~~";
            st.Delims.Add(new Delim { Marker = '~', Length = 0, Token = st.Tokens.Count - 1, Open = scanned.CanOpen, Close = scanned.CanClose });
        }
        st.Pos += scanned.Length;
        return true;
    }

    private static bool RuleEmphasis(State st)
    {
        char marker = st.Src[st.Pos];
        if (marker != '*' && marker != '_') return false;
        var scanned = ScanDelims(st, st.Pos, canSplitWord: marker == '*');
        for (int i = 0; i < scanned.Length; i++)
        {
            var tok = Push(st, "text");
            tok.Content = marker.ToString();
            st.Delims.Add(new Delim
            {
                Marker = marker,
                Length = scanned.Length,
                Token = st.Tokens.Count - 1,
                Open = scanned.CanOpen,
                Close = scanned.CanClose
            });
        }
        st.Pos += scanned.Length;
        return true;
    }

    private static bool RuleLink(State st)
    {
        if (st.Src[st.Pos] != '[' || st.LinkLevel > 0) return false;
        int start = st.Pos;
        int labelEnd = ParseLinkLabel(st.Src, st.Pos + 1);
        if (labelEnd < 0) return false;
        int pos = labelEnd + 1;
        if (pos >= st.PosMax || st.Src[pos] != '(') return false;
        pos++;
        while (pos < st.PosMax && IsMdWhiteSpace(CpAt(st.Src, pos))) pos++;

        var dest = ParseLinkDestination(st.Src, ref pos);
        if (!dest.Ok || !IsValidLink(dest.Value)) return false;
        string href = NormalizeLink(dest.Value);
        while (pos < st.PosMax && IsMdWhiteSpace(CpAt(st.Src, pos))) pos++;

        string? title = null;
        var t = ParseLinkTitle(st.Src, ref pos);
        if (t.Ok)
        {
            title = t.Value;
            while (pos < st.PosMax && IsMdWhiteSpace(CpAt(st.Src, pos))) pos++;
        }
        if (pos >= st.PosMax || st.Src[pos] != ')') return false;
        pos++;

        var openTok = Push(st, "link_open");
        openTok.Url = href;
        openTok.Title = title;

        // ラベル内部をインラインとして再帰解析（ネストした強調等はここで処理）
        int savedPos = st.Pos, savedMax = st.PosMax, savedLinkLevel = st.LinkLevel;
        var savedDelims = st.Delims;
        st.Pos = start + 1;
        st.PosMax = labelEnd;
        st.LinkLevel++;
        st.Delims = new List<Delim>();
        Tokenize(st);
        ProcessDelimiters(st.Delims);
        EmphasisPostProcess(st);
        StrikePostProcess(st);
        st.Delims = savedDelims;
        st.Pos = pos;
        st.PosMax = savedMax;
        st.LinkLevel = savedLinkLevel;

        Push(st, "link_close");
        return true;
    }

    private static bool RuleAutolink(State st)
    {
        if (st.Src[st.Pos] != '<') return false;
        int pos = st.Pos + 1;
        while (pos < st.PosMax && st.Src[pos] != '>' && st.Src[pos] != '<' && st.Src[pos] != '\n') pos++;
        if (pos >= st.PosMax || st.Src[pos] != '>') return false;
        string url = st.Src[(st.Pos + 1)..pos];
        string href;
        if (AutolinkUrlRe.IsMatch(url))
        {
            if (!IsValidLink(url)) return false;
            href = url;
        }
        else if (AutolinkEmailRe.IsMatch(url))
        {
            href = "mailto:" + url;
        }
        else return false;

        var openTok = Push(st, "link_open");
        openTok.Url = href;
        var textTok = Push(st, "text");
        textTok.Content = url;
        Push(st, "link_close");
        st.Pos = pos + 1;
        return true;
    }

    private static bool RuleEntity(State st)
    {
        if (st.Src[st.Pos] != '&') return false;
        int pos = st.Pos, max = st.PosMax;
        if (pos + 1 >= max) return false;
        if (st.Src[pos + 1] == '#')
        {
            var m = DigitalEntityRe.Match(st.Src, pos);
            if (!m.Success) return false;
            string body = m.Groups[1].Value;
            int code;
            try
            {
                code = body[0] == 'x' || body[0] == 'X' ? Convert.ToInt32(body[1..], 16) : int.Parse(body);
            }
            catch (FormatException)
            {
                return false;
            }
            if (code is < 1 or > 0x10FFFF or >= 0xD800 and <= 0xDFFF) return false;
            var tok = Push(st, "text");
            tok.Content = char.ConvertFromUtf32(code);
            st.Pos = pos + m.Length;
            return true;
        }
        int j = pos + 1;
        while (j < max && char.IsLetter(st.Src[j])) j++;
        if (j >= max || st.Src[j] != ';') return false;
        string name = st.Src[(pos + 1)..j];
        if (!NamedEntities.TryGetValue(name, out string? value) || value is null) return false;
        var tok2 = Push(st, "text");
        tok2.Content = value;
        st.Pos = j + 1;
        return true;
    }
    // ---- リンク解析ヘルパー（markdown-it の rules_inline/helpers を移植）----

    private static int ParseLinkLabel(string src, int start)
    {
        int max = src.Length, pos = start, level = 0;
        while (true)
        {
            if (pos >= max) return -1;
            char ch = src[pos];
            if (ch == '\\') { pos += 2; continue; }
            if (ch == ']') { if (level == 0) return pos; level--; }
            else if (ch == '[') level++;
            pos++;
        }
    }

    private static (bool Ok, string Value) ParseLinkDestination(string src, ref int pos)
    {
        int max = src.Length;
        while (pos < max && IsMdWhiteSpace(CpAt(src, pos))) pos++;
        if (pos >= max) return (false, "");
        if (src[pos] == '<')
        {
            pos++;
            var sb = new StringBuilder();
            while (true)
            {
                if (pos >= max) return (false, "");
                char ch = src[pos];
                if (ch == '>') { pos++; return (true, sb.ToString()); }
                // <> 内は空白を許容（改行のみ不可）。markdown-it と同じ。
                if (ch == '\n') return (false, "");
                if (ch == '\\' && pos + 1 < max && IsMdAsciiPunct(src[pos + 1]))
                {
                    sb.Append(src[pos + 1]); pos += 2; continue;
                }
                sb.Append(ch);
                pos++;
            }
        }
        int level = 0;
        var sb2 = new StringBuilder();
        while (true)
        {
            if (pos >= max) return (false, "");
            int code = CpAt(src, pos);
            if (code == 0x20 || IsMdWhiteSpace(code)) break;
            char ch = src[pos];
            if (code == '(') level++;
            else if (code == ')')
            {
                if (level == 0) break;
                level--;
            }
            else if (code == '\\')
            {
                if (pos + 1 < max && IsMdAsciiPunct(src[pos + 1]))
                {
                    sb2.Append(src[pos + 1]); pos += 2; continue;
                }
            }
            sb2.Append(ch);
            pos++;
        }
        string res = sb2.ToString();
        if (res.Length == 0) return (false, "");
        return (true, res);
    }

    private static (bool Ok, string Value) ParseLinkTitle(string src, ref int pos)
    {
        int max = src.Length;
        while (pos < max && IsMdWhiteSpace(CpAt(src, pos))) pos++;
        if (pos >= max) return (false, "");
        char quote = src[pos];
        if (quote != '\'' && quote != '(' && quote != '"') return (false, "");
        pos++;
        int start = pos;
        while (true)
        {
            if (pos >= max) return (false, "");
            char ch = src[pos];
            if (ch == '\\')
            {
                if (pos + 1 < max && IsMdAsciiPunct(src[pos + 1])) pos++;
            }
            else if (ch == quote)
            {
                string res = src[start..pos];
                pos++;
                return (true, res);
            }
            pos++;
        }
    }

    private static bool IsValidLink(string link)
    {
        if (string.IsNullOrEmpty(link)) return false;
        int colon = link.IndexOf(':');
        if (colon > 0 && DangerousProtocolRe.IsMatch(link[..colon])) return false;
        return true;
    }

    // mdurl.encode の既定文字集合（この他は英数字と既存の %XX エスケープを保持）。
    private const string LinkEncodeSafe = ";/?:@&=+$,-_.!~*'()#";

    // markdown-it の normalizeLink（mdurl.encode, keepEscaped=true）相当。
    private static string NormalizeLink(string url)
    {
        var sb = new StringBuilder(url.Length);
        for (int i = 0; i < url.Length; i++)
        {
            char c = url[i];
            if (c == '%' && i + 2 < url.Length && Uri.IsHexDigit(url[i + 1]) && Uri.IsHexDigit(url[i + 2]))
            {
                sb.Append(url, i, 3); i += 2; continue;
            }
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                || LinkEncodeSafe.IndexOf(c) >= 0)
            {
                sb.Append(c); continue;
            }
            int cp = CpAt(url, i);
            foreach (byte b in Encoding.UTF8.GetBytes(char.ConvertFromUtf32(cp)))
                sb.Append('%').Append(b.ToString("X2"));
            if (cp > 0xFFFF) i++;
        }
        return sb.ToString();
    }

    private static Dictionary<int, int> BuildBacktickRuns(string src)
    {
        var result = new Dictionary<int, int>();
        int pos = 0;
        while (true)
        {
            int start = src.IndexOf('`', pos);
            if (start < 0) break;
            int end = start + 1;
            while (end < src.Length && src[end] == '`') end++;
            result[end - start] = start; // 同じ長さなら最後の位置が勝つ（markdown-it と同じ）
            pos = end;
        }
        return result;
    }
    // ---- 強調マッチング（markdown-it の emphasis.js をそのまま移植）----

    private static void ProcessDelimiters(List<Delim> delimiters)
    {
        var openersBottom = new Dictionary<int, int[]>();
        int max = delimiters.Count;
        if (max == 0) return;
        int headerIdx = 0;
        int lastTokenIdx = -2;
        var jumps = new int[max];

        for (int closerIdx = 0; closerIdx < max; closerIdx++)
        {
            Delim closer = delimiters[closerIdx];
            if (delimiters[headerIdx].Marker != closer.Marker || lastTokenIdx != closer.Token - 1) headerIdx = closerIdx;
            lastTokenIdx = closer.Token;
            if (!closer.Close) continue;

            if (!openersBottom.TryGetValue(closer.Marker, out int[]? bottom))
            {
                bottom = new int[6] { -1, -1, -1, -1, -1, -1 };
                openersBottom[closer.Marker] = bottom;
            }
            int minOpenerIdx = bottom[(closer.Open ? 3 : 0) + closer.Length % 3];
            int openerIdx = headerIdx - jumps[headerIdx] - 1;
            int newMinOpenerIdx = openerIdx;

            while (openerIdx > minOpenerIdx)
            {
                Delim opener = delimiters[openerIdx];
                if (opener.Marker == closer.Marker && opener.Open && opener.End < 0)
                {
                    bool isOddMatch = false;
                    if (opener.Close || closer.Open)
                    {
                        if ((opener.Length + closer.Length) % 3 == 0)
                        {
                            if (opener.Length % 3 != 0 || closer.Length % 3 != 0) isOddMatch = true;
                        }
                    }
                    if (!isOddMatch)
                    {
                        int lastJump = openerIdx > 0 && !delimiters[openerIdx - 1].Open ? jumps[openerIdx - 1] + 1 : 0;
                        jumps[closerIdx] = closerIdx - openerIdx + lastJump;
                        jumps[openerIdx] = lastJump;
                        closer.Open = false;
                        opener.End = closerIdx;
                        opener.Close = false;
                        newMinOpenerIdx = -1;
                        lastTokenIdx = -2;
                        break;
                    }
                }
                openerIdx -= jumps[openerIdx] + 1;
            }
            if (newMinOpenerIdx != -1)
            {
                bottom[(closer.Open ? 3 : 0) + closer.Length % 3] = newMinOpenerIdx;
            }
        }
    }

    private static void EmphasisPostProcess(State st)
    {
        int max = st.Delims.Count;
        for (int i = max - 1; i >= 0; i--)
        {
            Delim startDelim = st.Delims[i];
            if (startDelim.Marker != '_' && startDelim.Marker != '*') continue;
            if (startDelim.End == -1) continue;
            Delim endDelim = st.Delims[startDelim.End];
            bool isStrong = i > 0
                && st.Delims[i - 1].End == startDelim.End + 1
                && st.Delims[i - 1].Marker == startDelim.Marker
                && st.Delims[i - 1].Token == startDelim.Token - 1
                && startDelim.End + 1 < max
                && st.Delims[startDelim.End + 1].Token == endDelim.Token + 1;
            var tokOpen = st.Tokens[startDelim.Token];
            tokOpen.Type = isStrong ? "strong_open" : "em_open";
            tokOpen.Content = "";
            var tokClose = st.Tokens[endDelim.Token];
            tokClose.Type = isStrong ? "strong_close" : "em_close";
            tokClose.Content = "";
            if (isStrong)
            {
                st.Tokens[st.Delims[i - 1].Token].Content = "";
                st.Tokens[st.Delims[startDelim.End + 1].Token].Content = "";
                i--;
            }
        }
    }

    private static void StrikePostProcess(State st)
    {
        int max = st.Delims.Count;
        for (int i = max - 1; i >= 0; i--)
        {
            Delim startDelim = st.Delims[i];
            if (startDelim.Marker != '~') continue;
            if (startDelim.End == -1) continue;
            Delim endDelim = st.Delims[startDelim.End];
            st.Tokens[startDelim.Token].Type = "s_open";
            st.Tokens[startDelim.Token].Content = "";
            st.Tokens[endDelim.Token].Type = "s_close";
            st.Tokens[endDelim.Token].Content = "";
        }
    }
    // ---- markdown-it-cjk-friendly の scanDelims 上書きを移植 ----
    // * と _ で挙動が異なる点に注意：CJK 補正は canSplitWord（= マーカーが *）のときのみ適用され、
    // _ は markdown-it 既定どおり「前後が標点記号」でのみ開閉できる。

    private static (bool CanOpen, bool CanClose, int Length) ScanDelims(State st, int start, bool canSplitWord)
    {
        char marker = st.Src[start];
        var lastChar = GetLastCp(st.Src, start);
        int lastMainChar = lastChar.Cp;
        int? twoPrevChar = null;
        if (IsNonEmojiGeneralUseVS(lastChar.Cp))
        {
            twoPrevChar = GetLastCp(st.Src, lastChar.Index).Cp;
            if (!IsZs(twoPrevChar.Value)) lastMainChar = twoPrevChar.Value;
        }

        int pos = start + 1;
        while (pos < st.PosMax && st.Src[pos] == marker) pos++;
        int count = pos - start;
        int nextChar = pos < st.PosMax ? CpAt(st.Src, pos) : 32;

        bool isLastWhiteSpace = IsMdWhiteSpace(lastMainChar);
        bool isNextWhiteSpace = IsMdWhiteSpace(nextChar);
        if (isLastWhiteSpace || isNextWhiteSpace)
            return (!isNextWhiteSpace, !isLastWhiteSpace, count);

        bool isLastPunctChar = IsMdAsciiPunct(lastMainChar) || IsPunctChar(lastMainChar);
        bool isNextPunctChar = IsMdAsciiPunct(nextChar) || IsPunctChar(nextChar);
        bool leftFlanking = isLastPunctChar;
        bool rightFlanking = isNextPunctChar;
        if (canSplitWord)
        {
            bool isEitherCjkChar = IsNextCjk(nextChar) ||
                (twoPrevChar is not null ? Is2PreviousCjk(twoPrevChar.Value, lastChar.Cp) : IsPreviousCjk(lastMainChar));
            leftFlanking |= isEitherCjkChar || !isNextPunctChar;
            rightFlanking |= isEitherCjkChar || !isLastPunctChar;
        }
        return (leftFlanking, rightFlanking, count);
    }

    private static (int Cp, int Index) GetLastCp(string s, int start)
    {
        if (start == 0) return (32, -1);
        char last = s[start - 1];
        if (char.IsLowSurrogate(last))
        {
            if (start >= 2 && char.IsHighSurrogate(s[start - 2]))
                return (char.ConvertToUtf32(s[start - 2], last), start - 2);
            return (last, start - 1);
        }
        return (last, start - 1);
    }

    /// <summary>位置 pos のコードポイント（代理対は結合して返す）。pos が末尾なら 32（空白）。</summary>
    private static int CpAt(string s, int pos)
    {
        if (pos >= s.Length) return 32;
        char c = s[pos];
        if (char.IsHighSurrogate(c) && pos + 1 < s.Length && char.IsLowSurrogate(s[pos + 1]))
            return char.ConvertToUtf32(c, s[pos + 1]);
        return c;
    }

    // ====================================================================
    // Unicode ユーティリティ（markdown-it の is_* と get-east-asian-width を移植）
    // ====================================================================

    private static bool IsMdWhiteSpace(int code) =>
        code >= 8192 && code <= 8202 ||
        code is 9 or 10 or 11 or 12 or 13 or 32 or 160 or 5760 or 8239 or 8287 or 12288;

    private static bool IsMdAsciiPunct(int code) =>
        (code >= 33 && code <= 47) || code is 58 or 59 || (code >= 60 && code <= 64) ||
        (code >= 91 && code <= 96) || (code >= 123 && code <= 126);

    private static bool IsPunctChar(int code) =>
        code < 0x10000 && IsPunctOrSymbol((char)code);

    private static bool IsPunctOrSymbol(char ch) => char.GetUnicodeCategory(ch) is
        System.Globalization.UnicodeCategory.OtherPunctuation
        or System.Globalization.UnicodeCategory.OpenPunctuation
        or System.Globalization.UnicodeCategory.ClosePunctuation
        or System.Globalization.UnicodeCategory.ConnectorPunctuation
        or System.Globalization.UnicodeCategory.DashPunctuation
        or System.Globalization.UnicodeCategory.FinalQuotePunctuation
        or System.Globalization.UnicodeCategory.InitialQuotePunctuation
        or System.Globalization.UnicodeCategory.MathSymbol
        or System.Globalization.UnicodeCategory.CurrencySymbol
        or System.Globalization.UnicodeCategory.ModifierSymbol
        or System.Globalization.UnicodeCategory.OtherSymbol;

    private static bool IsZs(int code) =>
        code < 0x10000 && char.GetUnicodeCategory((char)code) == System.Globalization.UnicodeCategory.SpaceSeparator;


    // ---- CJK 判定（markdown-it-cjk-friendly の isCjkBase 等を移植）----

    /// <summary>平坦な [start, end] ペア配列に cp が含まれるか（二分探索）。</summary>
    private static bool IsInRange(int[] ranges, int cp)
    {
        int lo = 0, hi = ranges.Length / 2 - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (cp < ranges[mid * 2]) hi = mid - 1;
            else if (cp > ranges[mid * 2 + 1]) lo = mid + 1;
            else return true;
        }
        return false;
    }

    /// <summary>
    /// isCjkBase(uc) の移植。null は「保留（IVS / ambiguous）」を意味し、
    /// 呼び出し側で ?? 相当のフォールバックを行う（JS と同じ）。
    /// </summary>
    private static bool? IsCjkBase(int uc)
    {
        if (uc < 4352) return false;
        // getCategory の判定順：ambiguous → fullwidth → halfwidth → narrow → wide → neutral
        if (IsInRange(EawAmbiguous, uc)) return null;
        if (IsInRange(EawFullwidth, uc)) return true;
        if (IsInRange(EawHalfwidth, uc)) return true;
        if (IsInRange(EawNarrow, uc)) return false;
        if (IsInRange(EawWide, uc)) return !IsInRange(EawEmojiInWide, uc); // wide → !isEmoji(uc)
        return IsInRange(EawHangulNeutral, uc); // neutral → /^\p{sc=Hangul}/
    }

    private static bool IsNextCjk(int uc) => IsCjkBase(uc) ?? false;

    private static bool IsPreviousCjk(int uc) => IsCjkBase(uc) ?? (uc >= 917760 && uc <= 917999);

    private static bool Is2PreviousCjk(int uc, int prev) =>
        IsCjkBase(uc) ?? (prev == 65025 && (uc is 8216 or 8217 or 8220 or 8221));
    private static bool IsNonEmojiGeneralUseVS(int uc) => uc >= 0xFE00 && uc <= 0xFE0E;
    // get-east-asian-width の lookup-data.js を hex 配列で移植（Node で機械生成）。
    private static readonly int[] EawAmbiguous =
    {
        0x00A1, 0x00A1, 0x00A4, 0x00A4, 0x00A7, 0x00A8, 0x00AA, 0x00AA, 0x00AD, 0x00AE,
        0x00B0, 0x00B4, 0x00B6, 0x00BA, 0x00BC, 0x00BF, 0x00C6, 0x00C6, 0x00D0, 0x00D0,
        0x00D7, 0x00D8, 0x00DE, 0x00E1, 0x00E6, 0x00E6, 0x00E8, 0x00EA, 0x00EC, 0x00ED,
        0x00F0, 0x00F0, 0x00F2, 0x00F3, 0x00F7, 0x00FA, 0x00FC, 0x00FC, 0x00FE, 0x00FE,
        0x0101, 0x0101, 0x0111, 0x0111, 0x0113, 0x0113, 0x011B, 0x011B, 0x0126, 0x0127,
        0x012B, 0x012B, 0x0131, 0x0133, 0x0138, 0x0138, 0x013F, 0x0142, 0x0144, 0x0144,
        0x0148, 0x014B, 0x014D, 0x014D, 0x0152, 0x0153, 0x0166, 0x0167, 0x016B, 0x016B,
        0x01CE, 0x01CE, 0x01D0, 0x01D0, 0x01D2, 0x01D2, 0x01D4, 0x01D4, 0x01D6, 0x01D6,
        0x01D8, 0x01D8, 0x01DA, 0x01DA, 0x01DC, 0x01DC, 0x0251, 0x0251, 0x0261, 0x0261,
        0x02C4, 0x02C4, 0x02C7, 0x02C7, 0x02C9, 0x02CB, 0x02CD, 0x02CD, 0x02D0, 0x02D0,
        0x02D8, 0x02DB, 0x02DD, 0x02DD, 0x02DF, 0x02DF, 0x0300, 0x036F, 0x0391, 0x03A1,
        0x03A3, 0x03A9, 0x03B1, 0x03C1, 0x03C3, 0x03C9, 0x0401, 0x0401, 0x0410, 0x044F,
        0x0451, 0x0451, 0x2010, 0x2010, 0x2013, 0x2016, 0x2018, 0x2019, 0x201C, 0x201D,
        0x2020, 0x2022, 0x2024, 0x2027, 0x2030, 0x2030, 0x2032, 0x2033, 0x2035, 0x2035,
        0x203B, 0x203B, 0x203E, 0x203E, 0x2074, 0x2074, 0x207F, 0x207F, 0x2081, 0x2084,
        0x20AC, 0x20AC, 0x2103, 0x2103, 0x2105, 0x2105, 0x2109, 0x2109, 0x2113, 0x2113,
        0x2116, 0x2116, 0x2121, 0x2122, 0x2126, 0x2126, 0x212B, 0x212B, 0x2153, 0x2154,
        0x215B, 0x215E, 0x2160, 0x216B, 0x2170, 0x2179, 0x2189, 0x2189, 0x2190, 0x2199,
        0x21B8, 0x21B9, 0x21D2, 0x21D2, 0x21D4, 0x21D4, 0x21E7, 0x21E7, 0x2200, 0x2200,
        0x2202, 0x2203, 0x2207, 0x2208, 0x220B, 0x220B, 0x220F, 0x220F, 0x2211, 0x2211,
        0x2215, 0x2215, 0x221A, 0x221A, 0x221D, 0x2220, 0x2223, 0x2223, 0x2225, 0x2225,
        0x2227, 0x222C, 0x222E, 0x222E, 0x2234, 0x2237, 0x223C, 0x223D, 0x2248, 0x2248,
        0x224C, 0x224C, 0x2252, 0x2252, 0x2260, 0x2261, 0x2264, 0x2267, 0x226A, 0x226B,
        0x226E, 0x226F, 0x2282, 0x2283, 0x2286, 0x2287, 0x2295, 0x2295, 0x2299, 0x2299,
        0x22A5, 0x22A5, 0x22BF, 0x22BF, 0x2312, 0x2312, 0x2460, 0x24E9, 0x24EB, 0x254B,
        0x2550, 0x2573, 0x2580, 0x258F, 0x2592, 0x2595, 0x25A0, 0x25A1, 0x25A3, 0x25A9,
        0x25B2, 0x25B3, 0x25B6, 0x25B7, 0x25BC, 0x25BD, 0x25C0, 0x25C1, 0x25C6, 0x25C8,
        0x25CB, 0x25CB, 0x25CE, 0x25D1, 0x25E2, 0x25E5, 0x25EF, 0x25EF, 0x2605, 0x2606,
        0x2609, 0x2609, 0x260E, 0x260F, 0x261C, 0x261C, 0x261E, 0x261E, 0x2640, 0x2640,
        0x2642, 0x2642, 0x2660, 0x2661, 0x2663, 0x2665, 0x2667, 0x266A, 0x266C, 0x266D,
        0x266F, 0x266F, 0x269E, 0x269F, 0x26BF, 0x26BF, 0x26C6, 0x26CD, 0x26CF, 0x26D3,
        0x26D5, 0x26E1, 0x26E3, 0x26E3, 0x26E8, 0x26E9, 0x26EB, 0x26F1, 0x26F4, 0x26F4,
        0x26F6, 0x26F9, 0x26FB, 0x26FC, 0x26FE, 0x26FF, 0x273D, 0x273D, 0x2776, 0x277F,
        0x2B56, 0x2B59, 0x3248, 0x324F, 0xE000, 0xF8FF, 0xFE00, 0xFE0F, 0xFFFD, 0xFFFD,
        0x1F100, 0x1F10A, 0x1F110, 0x1F12D, 0x1F130, 0x1F169, 0x1F170, 0x1F18D, 0x1F18F, 0x1F190,
        0x1F19B, 0x1F1AC, 0xE0100, 0xE01EF, 0xF0000, 0xFFFFD, 0x100000, 0x10FFFD,
    };

    private static readonly int[] EawFullwidth =
    {
        0x3000, 0x3000, 0xFF01, 0xFF60, 0xFFE0, 0xFFE6,
    };

    private static readonly int[] EawHalfwidth =
    {
        0x20A9, 0x20A9, 0xFF61, 0xFFBE, 0xFFC2, 0xFFC7, 0xFFCA, 0xFFCF, 0xFFD2, 0xFFD7,
        0xFFDA, 0xFFDC, 0xFFE8, 0xFFEE,
    };

    private static readonly int[] EawNarrow =
    {
        0x0020, 0x007E, 0x00A2, 0x00A3, 0x00A5, 0x00A6, 0x00AC, 0x00AC, 0x00AF, 0x00AF,
        0x27E6, 0x27ED, 0x2985, 0x2986,
    };

    private static readonly int[] EawWide =
    {
        0x1100, 0x115F, 0x231A, 0x231B, 0x2329, 0x232A, 0x23E9, 0x23EC, 0x23F0, 0x23F0,
        0x23F3, 0x23F3, 0x25FD, 0x25FE, 0x2614, 0x2615, 0x2630, 0x2637, 0x2648, 0x2653,
        0x267F, 0x267F, 0x268A, 0x268F, 0x2693, 0x2693, 0x26A1, 0x26A1, 0x26AA, 0x26AB,
        0x26BD, 0x26BE, 0x26C4, 0x26C5, 0x26CE, 0x26CE, 0x26D4, 0x26D4, 0x26EA, 0x26EA,
        0x26F2, 0x26F3, 0x26F5, 0x26F5, 0x26FA, 0x26FA, 0x26FD, 0x26FD, 0x2705, 0x2705,
        0x270A, 0x270B, 0x2728, 0x2728, 0x274C, 0x274C, 0x274E, 0x274E, 0x2753, 0x2755,
        0x2757, 0x2757, 0x2795, 0x2797, 0x27B0, 0x27B0, 0x27BF, 0x27BF, 0x2B1B, 0x2B1C,
        0x2B50, 0x2B50, 0x2B55, 0x2B55, 0x2E80, 0x2E99, 0x2E9B, 0x2EF3, 0x2F00, 0x2FD5,
        0x2FF0, 0x2FFF, 0x3001, 0x303E, 0x3041, 0x3096, 0x3099, 0x30FF, 0x3105, 0x312F,
        0x3131, 0x318E, 0x3190, 0x31E5, 0x31EF, 0x321E, 0x3220, 0x3247, 0x3250, 0xA48C,
        0xA490, 0xA4C6, 0xA960, 0xA97C, 0xAC00, 0xD7A3, 0xF900, 0xFAFF, 0xFE10, 0xFE19,
        0xFE30, 0xFE52, 0xFE54, 0xFE66, 0xFE68, 0xFE6B, 0x16FE0, 0x16FE4, 0x16FF0, 0x16FF6,
        0x17000, 0x18CD5, 0x18CFF, 0x18D1E, 0x18D80, 0x18DF2, 0x1AFF0, 0x1AFF3, 0x1AFF5, 0x1AFFB,
        0x1AFFD, 0x1AFFE, 0x1B000, 0x1B122, 0x1B132, 0x1B132, 0x1B150, 0x1B152, 0x1B155, 0x1B155,
        0x1B164, 0x1B167, 0x1B170, 0x1B2FB, 0x1D300, 0x1D356, 0x1D360, 0x1D376, 0x1F004, 0x1F004,
        0x1F0CF, 0x1F0CF, 0x1F18E, 0x1F18E, 0x1F191, 0x1F19A, 0x1F200, 0x1F202, 0x1F210, 0x1F23B,
        0x1F240, 0x1F248, 0x1F250, 0x1F251, 0x1F260, 0x1F265, 0x1F300, 0x1F320, 0x1F32D, 0x1F335,
        0x1F337, 0x1F37C, 0x1F37E, 0x1F393, 0x1F3A0, 0x1F3CA, 0x1F3CF, 0x1F3D3, 0x1F3E0, 0x1F3F0,
        0x1F3F4, 0x1F3F4, 0x1F3F8, 0x1F43E, 0x1F440, 0x1F440, 0x1F442, 0x1F4FC, 0x1F4FF, 0x1F53D,
        0x1F54B, 0x1F54E, 0x1F550, 0x1F567, 0x1F57A, 0x1F57A, 0x1F595, 0x1F596, 0x1F5A4, 0x1F5A4,
        0x1F5FB, 0x1F64F, 0x1F680, 0x1F6C5, 0x1F6CC, 0x1F6CC, 0x1F6D0, 0x1F6D2, 0x1F6D5, 0x1F6D8,
        0x1F6DC, 0x1F6DF, 0x1F6EB, 0x1F6EC, 0x1F6F4, 0x1F6FC, 0x1F7E0, 0x1F7EB, 0x1F7F0, 0x1F7F0,
        0x1F90C, 0x1F93A, 0x1F93C, 0x1F945, 0x1F947, 0x1F9FF, 0x1FA70, 0x1FA7C, 0x1FA80, 0x1FA8A,
        0x1FA8E, 0x1FAC6, 0x1FAC8, 0x1FAC8, 0x1FACD, 0x1FADC, 0x1FADF, 0x1FAEA, 0x1FAEF, 0x1FAF8,
        0x20000, 0x2FFFD, 0x30000, 0x3FFFD,
    };

// wide かつ Emoji_Presentation のコードポイント（isCjkBase: wide → !isEmoji）
    private static readonly int[] EawEmojiInWide =
    {
        0x231A, 0x231B, 0x23E9, 0x23EC, 0x23F0, 0x23F0, 0x23F3, 0x23F3, 0x25FD, 0x25FE,
        0x2614, 0x2615, 0x2648, 0x2653, 0x267F, 0x267F, 0x2693, 0x2693, 0x26A1, 0x26A1,
        0x26AA, 0x26AB, 0x26BD, 0x26BE, 0x26C4, 0x26C5, 0x26CE, 0x26CE, 0x26D4, 0x26D4,
        0x26EA, 0x26EA, 0x26F2, 0x26F3, 0x26F5, 0x26F5, 0x26FA, 0x26FA, 0x26FD, 0x26FD,
        0x2705, 0x2705, 0x270A, 0x270B, 0x2728, 0x2728, 0x274C, 0x274C, 0x274E, 0x274E,
        0x2753, 0x2755, 0x2757, 0x2757, 0x2795, 0x2797, 0x27B0, 0x27B0, 0x27BF, 0x27BF,
        0x2B1B, 0x2B1C, 0x2B50, 0x2B50, 0x2B55, 0x2B55, 0x1F004, 0x1F004, 0x1F0CF, 0x1F0CF,
        0x1F18E, 0x1F18E, 0x1F191, 0x1F19A, 0x1F201, 0x1F201, 0x1F21A, 0x1F21A, 0x1F22F, 0x1F22F,
        0x1F232, 0x1F236, 0x1F238, 0x1F23A, 0x1F250, 0x1F251, 0x1F300, 0x1F320, 0x1F32D, 0x1F335,
        0x1F337, 0x1F37C, 0x1F37E, 0x1F393, 0x1F3A0, 0x1F3CA, 0x1F3CF, 0x1F3D3, 0x1F3E0, 0x1F3F0,
        0x1F3F4, 0x1F3F4, 0x1F3F8, 0x1F43E, 0x1F440, 0x1F440, 0x1F442, 0x1F4FC, 0x1F4FF, 0x1F53D,
        0x1F54B, 0x1F54E, 0x1F550, 0x1F567, 0x1F57A, 0x1F57A, 0x1F595, 0x1F596, 0x1F5A4, 0x1F5A4,
        0x1F5FB, 0x1F64F, 0x1F680, 0x1F6C5, 0x1F6CC, 0x1F6CC, 0x1F6D0, 0x1F6D2, 0x1F6D5, 0x1F6D7,
        0x1F6DD, 0x1F6DF, 0x1F6EB, 0x1F6EC, 0x1F6F4, 0x1F6FC, 0x1F7E0, 0x1F7EB, 0x1F7F0, 0x1F7F0,
        0x1F90C, 0x1F93A, 0x1F93C, 0x1F945, 0x1F947, 0x1F9FF, 0x1FA70, 0x1FA74, 0x1FA78, 0x1FA7C,
        0x1FA80, 0x1FA86, 0x1FA90, 0x1FAAC, 0x1FAB0, 0x1FABA, 0x1FAC0, 0x1FAC5, 0x1FAD0, 0x1FAD9,
        0x1FAE0, 0x1FAE7, 0x1FAF0, 0x1FAF6,
    };

// neutral かつ sc=Hangul のコードポイント（isCjkBase: neutral → Hangul チェック）
    private static readonly int[] EawHangulNeutral =
    {
        0x1160, 0x11FF, 0xD7B0, 0xD7C6, 0xD7CB, 0xD7FB,
    };

    // ---- フラットなトークン列を AST に変換 ----

    private static List<MdInline> BuildTree(List<Tok> tokens)
    {
        var root = new List<MdInline>();
        var stack = new Stack<List<MdInline>>();
        stack.Push(root);
        int i = 0;
        while (i < tokens.Count)
        {
            var tok = tokens[i];
            switch (tok.Type)
            {
                case "text":
                    AppendText(stack.Peek(), tok.Content);
                    i++;
                    break;
                case "softbreak":
                    stack.Peek().Add(new MdSoftBreak());
                    i++;
                    break;
                case "hardbreak":
                    stack.Peek().Add(new MdHardBreak());
                    i++;
                    break;
                case "code":
                    stack.Peek().Add(new MdCodeSpan { Value = tok.Content });
                    i++;
                    break;
                case "em_open":
                case "strong_open":
                case "s_open":
                case "link_open":
                {
                    MdInline node = tok.Type switch
                    {
                        "em_open" => new MdEm(),
                        "strong_open" => new MdStrong(),
                        "s_open" => new MdStrike(),
                        _ => new MdLink { Url = tok.Url ?? "", Title = tok.Title }
                    };
                    stack.Peek().Add(node);
                    var children = node switch
                    {
                        MdEm e => e.Children,
                        MdStrong s => s.Children,
                        MdStrike k => k.Children,
                        _ => ((MdLink)node).Children
                    };
                    stack.Push(children);
                    i++;
                    break;
                }
                case "em_close":
                case "strong_close":
                case "s_close":
                case "link_close":
                    stack.Pop();
                    i++;
                    break;
                default:
                    AppendText(stack.Peek(), tok.Content);
                    i++;
                    break;
            }
        }
        return root;
    }

    private static void AppendText(List<MdInline> list, string text)
    {
        if (text.Length == 0) return;
        if (list.Count > 0 && list[^1] is MdText last) last.Value += text;
        else list.Add(new MdText { Value = text });
    }
}