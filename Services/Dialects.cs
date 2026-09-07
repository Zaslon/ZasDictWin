using System.Text;
using System.Text.RegularExpressions;

namespace ZasDictWin.Services;

public sealed record DialectResult(string Sekore, string Titauini, string Kaiko, string Arzafire);

/// <summary>
/// zasdict.lang.dialects の移植。関数の分割と適用順は Python 版に合わせてある。
/// 入力の強勢母音は大文字、語頭は #、語末は φ で表す内部表現を使う。
/// </summary>
public static class Dialects
{
    /// <summary>
    /// titauini() は Python 版で「3母音化」の結果が次の行に上書きされて捨てられている。
    /// 出力を既存データと一致させるため既定では同じ挙動を保つ。false にすると 3母音化が効く。
    /// </summary>
    public static bool FaithfulTitauini { get; set; } = true;

    public static DialectResult Convert(string word)
    {
        var processed = Ortho1("#" + word + "φ");

        var ce = CommonE(processed);
        var cf = CommonF(processed);

        string Either(Func<string, string> f) =>
            ce == cf ? f(ce) : $"{f(ce)}または{f(cf)}";

        var arzafireWord = Arzafire(processed);
        var ceA = CommonE(arzafireWord);
        var cfA = CommonF(arzafireWord);
        var arzafire = ceA == cfA ? Sekore(ceA) : $"{Sekore(ceA)}または{Sekore(cfA)}";

        return new DialectResult(Either(Sekore), Either(Titauini), Either(Kaiko), arzafire);
    }

    // ---- 共通処理 --------------------------------------------------------

    /// <summary>大文字・語頭語末マーカーを落とし、多義になる字を選択肢の形にする。</summary>
    public static string Strip(string w)
    {
        var x = w.Replace("#", "")
                 .Replace("φ", "")
                 .Replace("e", "(a|i)")
                 .Replace("o", "(e|a)")
                 .Replace("q", "(b|u)")
                 .Replace("x", "(sa|s'i)")
                 .Replace("l", "(r'a|ri)");
        return x.ToLowerInvariant();
    }

    /// <summary>前処理。強勢を数字に退避してから小文字化するので、強勢だけが大文字で残る。</summary>
    public static string Ortho1(string w)
    {
        var x = Translate(w, "AIUEO", "12345");
        x = x.ToLowerInvariant();
        x = Translate(x, "12345", "AIUEO");
        x = x.Replace("ki", "kyi").Replace("kI", "kyI")
             .Replace("sh", "sy").Replace("si", "syi").Replace("sI", "syI")
             .Replace("ti", "tyi").Replace("tI", "tyI")
             .Replace("ch", "ty")
             .Replace("ts", "c").Replace("tu", "cu").Replace("tU", "cU")
             .Replace("fu", "hu").Replace("fU", "hU")
             .Replace("dh", "dy")
             .Replace("j", "zy");
        return x;
    }

    public static string Ortho2(string w)
    {
        var x = Regex.Replace(w, "([stnzdbrSTNZDBR])y", "$1'");
        x = x.Replace("#y", "#i");
        return Translate(x, "yw", "iu");
    }

    public static string CommonF(string w) => Common(w, "$1φ");

    public static string CommonE(string w) => Common(w, "$1eφ");

    private static string Common(string w, string finalReplacement)
    {
        var x = w.Replace("#hu", "#fu");
        x = Regex.Replace(x, @"(\#)h([^y])", "$1$2");
        x = Translate(x, "AIUEO", "EAOIU");
        x = Translate(x, "aiueo", "uiaeo");
        return Regex.Replace(x, "([^c])uφ", finalReplacement);
    }

    // ---- 方言 ------------------------------------------------------------

    /// <summary>旗艦方言</summary>
    public static string Sekore(string w)
    {
        // C1強勢時
        var x = Regex.Replace(w, "h([AIUEO])", "F$1");
        x = Regex.Replace(x, "r(y*?)([AIUEO])", "D$1$2");
        // C2強勢時
        x = Regex.Replace(x, "([AIUEO])[uw]", "$1V");
        x = Regex.Replace(x, "([AIUEO])t", "$1C");
        x = Regex.Replace(x, "([AIUEO])r", "$1D");
        // 強勢VC後C1
        x = Regex.Replace(x, "([AIUEO])[sc]", "$1Z");
        x = Regex.Replace(x, "([AIUEO])t", "$1D");
        x = Regex.Replace(x, "([AIUEO])f", "$1V");
        x = Regex.Replace(x, "([AIUEO])[kh]", "$1G");
        x = Regex.Replace(x, "([AIUEO])p", "$1B");
        // C1の子音変化
        x = Regex.Replace(x, "p(y*?)([aiueo])", "f$1$2");
        x = Regex.Replace(x, "v(y*?)([aiueo])", "u$1$2");
        x = Regex.Replace(x, "d(y*?)([aiueo])", "r$1$2");
        x = Regex.Replace(x, "[kg](y*?)([aiueo])", "h$1$2");
        // C2の子音変化
        x = Regex.Replace(x, "([aiueo])f", "$1p");
        x = Regex.Replace(x, "([aiueo])[td]", "$1r");
        x = Regex.Replace(x, "([aiueo])v", "$1u");
        x = Regex.Replace(x, "([aiueo])g", "$1h");
        // 強勢のない半母音の母音化
        x = Regex.Replace(x, "[y']([^AIUEO])", "$1");
        // ts → c
        x = Regex.Replace(x, "[tT][sS]", "c");
        return Strip(Ortho2(x));
    }

    /// <summary>資源循環艦方言</summary>
    public static string Titauini(string w)
    {
        var threeVowel = Translate(w, "oOE", "eUI");
        var x = Translate(FaithfulTitauini ? w : threeVowel, "jdbw", "drwq");
        return Strip(Ortho2(x));
    }

    /// <summary>探査艦方言</summary>
    public static string Kaiko(string w)
    {
        // s, r 変化
        var x = Regex.Replace(w, "s([ieIE])", "sy$1");
        x = x.Replace("se", "x");
        x = Regex.Replace(x, "r([auAU])", "ry$1");
        x = x.Replace("ro", "l");
        // 強勢音節母音変化
        x = x.Replace("Easi", "AU").Replace("A", "AI").Replace("O", "EI").Replace("U", "OU");
        // 語末子音削除
        x = Regex.Replace(x, "[^aiueoAIUEO]φ", "φ");
        // 子音変化
        x = Regex.Replace(x, "g([aiueoAIUEO])", "ny$1");
        x = Translate(x, "vh", "uu");
        x = x.Replace("zy", "i");
        // 連続子音変化
        x = Regex.Replace(x, "[^aiueoAIUEOxly#]([^aiueoAIUEOxly])", "$1$1");
        x = Regex.Replace(x, "[^aiueoAIUEOxly#]x", "sx");
        x = Regex.Replace(x, "[^aiueoAIUEOxly#]l", "rl");
        return Strip(Ortho2(x));
    }

    /// <summary>教団暗号への変換準備。結果は sekore に通して仕上げる。</summary>
    public static string Arzafire(string w)
    {
        var x = Translate(w, "aiueoAIUEO", "iueoaIUEOA");
        x = Translate(x, "kstnhmyrwgzdbp", "stnhmrrrkzdbgp");
        return x.Replace("pr", "py").Replace("sr", "sy").Replace("nr", "ny")
                .Replace("hr", "hy").Replace("mr", "my").Replace("rr", "ry")
                .Replace("zr", "zy").Replace("dr", "dy").Replace("gr", "gy");
    }

    /// <summary>Python の str.translate(str.maketrans(from, to)) 相当。</summary>
    private static string Translate(string s, string from, string to)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            var i = from.IndexOf(c);
            sb.Append(i >= 0 ? to[i] : c);
        }
        return sb.ToString();
    }
}
