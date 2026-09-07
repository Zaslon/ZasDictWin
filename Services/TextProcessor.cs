using System.Collections.Concurrent;
using ZasDictWin.Models;

namespace ZasDictWin.Services;

/// <summary>
/// イジェール語のカスタム字順による比較。デスクトップ版 func.py の compare_forms 相当。
/// </summary>
public sealed class TextProcessor
{
    public const string DefaultSortOrder = "eaoiuhkstcnrmpfgzdbv- ";

    private static readonly ConcurrentDictionary<string, Dictionary<char, int>> RankCache = new();

    public string SortOrder { get; }
    public string Punctuations { get; }

    private readonly Dictionary<char, int> _rank;

    public TextProcessor(string? sortOrder, string? punctuations)
    {
        SortOrder = string.IsNullOrEmpty(sortOrder) ? DefaultSortOrder : sortOrder;
        Punctuations = punctuations ?? "";
        _rank = RankCache.GetOrAdd(SortOrder, static order =>
        {
            var map = new Dictionary<char, int>();
            for (var i = 0; i < order.Length; i++) map.TryAdd(order[i], i);
            return map;
        });
    }

    /// <summary>字順表にない文字は表の後ろにコードポイント順で並べる。</summary>
    private int Rank(char c) => _rank.TryGetValue(c, out var i) ? i : _rank.Count + c;

    public string Normalize(string s)
    {
        s = s.ToLowerInvariant();
        if (Punctuations.Length == 0) return s;
        Span<char> buf = s.Length <= 256 ? stackalloc char[s.Length] : new char[s.Length];
        var n = 0;
        foreach (var c in s)
            if (Punctuations.IndexOf(c) < 0) buf[n++] = c;
        return new string(buf[..n]);
    }

    public int CompareForms(string a, string b)
    {
        var x = Normalize(a);
        var y = Normalize(b);
        var len = Math.Min(x.Length, y.Length);
        for (var i = 0; i < len; i++)
        {
            var d = Rank(x[i]) - Rank(y[i]);
            if (d != 0) return d;
        }
        return x.Length - y.Length;
    }

    public IComparer<Word> WordComparer => Comparer<Word>.Create((a, b) =>
    {
        var d = CompareForms(a.Form, b.Form);
        // 同綴りは登録順（id 昇順）で安定させ、同音異義語の連番と一致させる。
        return d != 0 ? d : a.Id.CompareTo(b.Id);
    });

    /// <summary>同綴りの単語に 2 以降の連番を振る。並び順は呼び出し側の順序に従う。</summary>
    public static void AssignHomonymIndexes(IEnumerable<Word> orderedWords)
    {
        var counter = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var w in orderedWords)
        {
            counter.TryGetValue(w.Form, out var n);
            counter[w.Form] = ++n;
            w.HomonymIndex = n;
        }
    }
}
