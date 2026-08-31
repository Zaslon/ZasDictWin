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
            try { _ignored = new Regex(ignoredPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant); }
            catch (ArgumentException ex)
            {
                // 壊れた正規表現は無視して検索自体は動かす。画面に出す先が無いので記録に残す。
                ErrorLog.Write($"ignoredPattern の解釈 ({ignoredPattern})", ex);
                _ignored = null;
            }
        }
    }

    /// <summary>
    /// ignoredPattern は前方/後方/完全一致の判定時にだけ見出し語から除去する。
    /// 部分一致で除去すると入力した記号が絶対にヒットしなくなるため対象外。
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
        if (scope is SearchScope.Form or SearchScope.Both or SearchScope.FullText)
        {
            if (Hit(w.Form, loweredQuery, mode, stripIgnored: true)) return true;
            if (scope == SearchScope.FullText)
                foreach (var v in w.Variations)
                    if (Hit(v.Form, loweredQuery, mode, stripIgnored: true)) return true;
        }

        if (scope is SearchScope.Translation or SearchScope.Both or SearchScope.FullText)
        {
            foreach (var t in w.Translations)
            {
                if (Hit(t.Title, loweredQuery, mode, stripIgnored: false)) return true;
                foreach (var f in t.Forms)
                    if (Hit(f, loweredQuery, mode, stripIgnored: false)) return true;
            }
        }

        if (scope == SearchScope.FullText)
        {
            foreach (var tag in w.Tags)
                if (Hit(tag, loweredQuery, mode, stripIgnored: false)) return true;
            foreach (var c in w.Contents)
            {
                if (Hit(c.Title, loweredQuery, mode, stripIgnored: false)) return true;
                if (Hit(c.Text, loweredQuery, mode, stripIgnored: false)) return true;
            }
            foreach (var r in w.Relations)
                if (Hit(r.Form, loweredQuery, mode, stripIgnored: false)) return true;
        }

        return false;
    }

    private bool Hit(string target, string loweredQuery, SearchMode mode, bool stripIgnored)
    {
        if (string.IsNullOrEmpty(target)) return false;
        var t = target.ToLowerInvariant();
        if (stripIgnored && mode != SearchMode.Partial) t = StripIgnored(t);

        return mode switch
        {
            SearchMode.Forward => t.StartsWith(loweredQuery, StringComparison.Ordinal),
            SearchMode.Backward => t.EndsWith(loweredQuery, StringComparison.Ordinal),
            SearchMode.Exact => string.Equals(t, loweredQuery, StringComparison.Ordinal),
            _ => t.Contains(loweredQuery, StringComparison.Ordinal)
        };
    }
}
