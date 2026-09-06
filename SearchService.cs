using System.Text.RegularExpressions;
using ZasDictWin.Models;

namespace ZasDictWin.Services;

public enum SearchMode { Forward, Partial, Backward, Exact }

public enum SearchScope { Form, Translation, Both, FullText }

public sealed class SearchService
{
    private readonly TextProcessor _text;
    private readonly Regex? _ignored;

    public SearchService(TextProcessor text, string? ignoredPattern)
    {
        _text = text;
        if (!string.IsNullOrWhiteSpace(ignoredPattern))
        {
            // 照合は常に小文字化した文字列に対して行うため、パターン側の大小は問わない。
            try
            {
                _ignored = new Regex(ignoredPattern,
                    RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            }
            catch (ArgumentException ex)
            {
                // 壊れた正規表現は無視して検索自体は動かす。画面に出す先が無いので記録に残す。
                ErrorLog.Write($"ignoredPattern の解釈 ({ignoredPattern})", ex);
                _ignored = null;
            }
        }
    }

    /// <summary>
    /// ignoredPattern に当たった部分を落とす。訳語に付く「【音】」や「(地面など)」といった注釈を
    /// 読み飛ばして引くための設定なので、見出し語に限らず照合する文字列に当てる（全文検索を除く）。
    /// </summary>
    private string StripIgnored(string s) => _ignored is null ? s : _ignored.Replace(s, "");

    public IEnumerable<Word> Filter(IEnumerable<Word> words, string query, SearchMode mode, SearchScope scope)
    {
        if (string.IsNullOrEmpty(query)) return words;
        var q = query.ToLowerInvariant();
        return words.Where(w => Matches(w, q, mode, scope));
    }

    public bool Matches(Word w, string loweredQuery, SearchMode mode, SearchScope scope)
    {
        // 全文検索は書かれている文字列そのものを探す場なので、無視パターンは当てない。
        var useIgnored = scope != SearchScope.FullText;
        // 検索語からも同じ規則で削る。対象側だけ削ると、無視対象の記号を入力に含めた途端に
        // どの語にも当たらなくなる。
        var q = useIgnored ? StripIgnored(loweredQuery) : loweredQuery;

        if (scope is SearchScope.Form or SearchScope.Both or SearchScope.FullText)
        {
            if (Hit(w.Form, q, mode, useIgnored)) return true;
            if (scope == SearchScope.FullText)
                foreach (var v in w.Variations)
                    if (Hit(v.Form, q, mode, useIgnored)) return true;
        }

        if (scope is SearchScope.Translation or SearchScope.Both or SearchScope.FullText)
        {
            foreach (var t in w.Translations)
            {
                if (Hit(t.Title, q, mode, useIgnored)) return true;
                foreach (var f in t.Forms)
                    if (Hit(f, q, mode, useIgnored)) return true;
            }
        }

        if (scope == SearchScope.FullText)
        {
            foreach (var tag in w.Tags)
                if (Hit(tag, q, mode, useIgnored)) return true;
            foreach (var c in w.Contents)
            {
                if (Hit(c.Title, q, mode, useIgnored)) return true;
                if (Hit(c.Text, q, mode, useIgnored)) return true;
            }
            foreach (var r in w.Relations)
                if (Hit(r.Form, q, mode, useIgnored)) return true;
        }

        return false;
    }

    private bool Hit(string target, string query, SearchMode mode, bool useIgnored)
    {
        if (string.IsNullOrEmpty(target)) return false;
        var t = target.ToLowerInvariant();
        if (useIgnored) t = StripIgnored(t);

        return mode switch
        {
            SearchMode.Forward => t.StartsWith(query, StringComparison.Ordinal),
            SearchMode.Backward => t.EndsWith(query, StringComparison.Ordinal),
            SearchMode.Exact => string.Equals(t, query, StringComparison.Ordinal),
            _ => t.Contains(query, StringComparison.Ordinal)
        };
    }
}
