using System.IO;
using System.Text;

namespace ZasDictWin.Services;

public sealed record ChangeEntry(DateTime At, string Operation, string Form, string Detail);

/// <summary>
/// &lt;辞書名&gt;_changelog.csv への追記。辞書の保存が成功したタイミングでだけフラッシュする。
/// </summary>
public static class ChangelogService
{
    private const string Header = "日時,操作,見出し語,内容";

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
        if (isNew) sb.AppendLine(Header);
        foreach (var e in list)
        {
            sb.Append(Escape(e.At.ToString("yyyy-MM-dd HH:mm:ss"))).Append(',')
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
