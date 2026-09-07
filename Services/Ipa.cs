using System.Text.RegularExpressions;

namespace ZasDictWin.Services;

/// <summary>
/// zasdict.lang.ipa の移植。IPA 表記をイジェール語の音写に変換する。
/// 置換は上から順に適用するため並び順に意味がある（例: ɐ は母音 e の行で先に消費される）。
/// </summary>
public static class Ipa
{
    private static readonly (string Pattern, string Replacement)[] Rules =
    {
        // 母音
        ("`|ˈ|ˌ", ""),
        ("i|ɨ", "i"),
        ("e|ɘ|e̞|ɛ|æ|ɜ|ɐ|œ|ɪ|ɪ̈", "e"),
        ("ə", "(e|o)"),
        ("ɐ", "(e|a)"),
        ("ʊ|ø̞|o|ɤ̞|o̞|ʌ|ɔ", "o"),
        ("a|ɶ|ä|ɑ|ɒ", "a"),
        ("y|ʉ|ɯ|u|ʏ|ʊ̈|ɯ̽", "u"),

        // 子音
        ("p|p̪", "p"),
        ("t|t̪", "t"),
        ("ʈ|c", "t'"),
        ("k", "k"),
        ("b|b̪", "b"),
        ("d̪|d", "d"),
        ("ɖ|ɟ", "d'"),
        ("g", "g"),
        ("m̥|m|ɱ̊|ɱ", "m"),
        ("n̪̊|n̪|n̥|n", "n"),
        ("ɳ|ɲ", "n'"),
        ("ŋ", "g"),
        ("r̥|r|ɹ̥|ɹ", "r'"),
        ("ⱱ̟|ⱱ|ɸ|f|β̞|ʋ̥|ʋ", "f"),
        ("ɾ|ɽ|ɟ̆", "r"),
        ("β|v", "v"),
        ("θ|s|ʃ", "s"),
        ("ð|z|ʒ", "z"),
        ("ʂ|ç|x", "s'"),
        ("ʐ|ʝ|ɣ", "z'"),
        ("χ", "h"),
        ("ʁ", "g")
    };

    public static string ToSpelling(string ipa)
    {
        var w = ipa;
        foreach (var (pattern, replacement) in Rules)
            w = Regex.Replace(w, pattern, replacement);
        return w;
    }
}
