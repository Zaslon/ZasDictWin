using System.IO;
using System.Text;

namespace ZasDictWin.Services;

public sealed record ChangeEntry(DateTime At, string Operation, string Form, string Detail);

/// <summary>
/// &lt;辞書名&gt;_changelog.csv への追記。辞書の保存が成功したタイミングでだけフラッシュする。
/// </summary>
public static class ChangelogService
{
    /// <summary>CSV の列名。書き出し時の見出し行と、更新履歴欄の固定ヘッダーの両方で使う。</summary>
    public static readonly string[] DefaultHeader = { "timestamp", "type", "form", "details" };

    public static string DefaultPathFor(string dictionaryPath)
    {
        var dir = Path.GetDirectoryName(dictionaryPath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(dictionaryPath);
        return Path.Combine(dir, $"{name}_changelog.csv");
    }

    public static void Append(string csvPath, IEnumerable<ChangeEntry> entries)
    {
        var list = entries.ToList();
        if (list.Count == 0) return;

        var isNew = !File.Exists(csvPath);
        var sb = new StringBuilder();
        if (isNew) sb.AppendLine(string.Join(',', DefaultHeader));
        foreach (var e in list)
        {
            sb.Append(Escape(e.At.ToString("yyyy-MM-dd"))).Append(',')
              .Append(Escape(e.Operation)).Append(',')
              .Append(Escape(e.Form)).Append(',')
              .Append(Escape(e.Detail)).AppendLine();
        }

        // Excel が既定で開けるよう BOM 付き UTF-8。既存ファイルには追記のみ。
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: isNew);
        File.AppendAllText(csvPath, sb.ToString(), encoding);
    }

    public static IReadOnlyList<string[]> Read(string csvPath, int maxRows = 500)
    {
        if (!File.Exists(csvPath)) return Array.Empty<string[]>();
        var rows = new List<string[]>();
        foreach (var line in File.ReadLines(csvPath, Encoding.UTF8))
        {
            rows.Add(ParseLine(line));
            if (rows.Count >= maxRows + 1) break;
        }
        return rows;
    }

    /// <summary>
    /// CSV の 1 行目が見出し行かどうか。このアプリは必ず先頭に見出し行を書きますが、
    /// 手作業で作った CSV でも判別できるよう「日付（yyyy-MM-dd）で始まらない行」を見出しとみなします。
    /// </summary>
    public static bool IsHeaderRow(string[] cells) => cells.Length == 0 || !StartsWithDate(cells[0]);

    private static bool StartsWithDate(string s)
    {
        if (s.StartsWith('*')) s = s[1..];   // 未フラッシュの履歴は行頭に * を付けて表示している
        return s.Length >= 10 && char.IsDigit(s[0]) && char.IsDigit(s[1])
                            && char.IsDigit(s[2]) && char.IsDigit(s[3])
                 && (s[4] == '-' || s[4] == '/');
    }

    private static string Escape(string s)
    {
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return '"' + s.Replace("\"", "\"\"") + '"';
    }

    private static string[] ParseLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }
}
