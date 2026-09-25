using ZasDictWin.ViewModels;

namespace ZasDictWin.Mediator;

/// <summary>ブラウザのアドレス欄に打った文字の解釈。</summary>
public static class BrowserAddress
{
    private const string GoogleSearch = "https://www.google.com/search?q=";

    /// <summary>アドレス欄の文字を URL に寄せる。scheme 無しは https、語句だけなら検索にする。</summary>
    public static string Normalize(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return BrowserViewModel.FallbackStartUrl;
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return s;

        // 「動詞 変換」のような語句と「dict.example.com」を区別する。
        if (s.Any(char.IsWhiteSpace) || (!s.Contains('.') && !s.Contains('/')))
            return GoogleSearch + Uri.EscapeDataString(s);

        return "https://" + s;
    }
}
