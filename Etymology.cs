using System.Text;

namespace ZasDictWin.Services;

/// <summary>語源欄の 1 区間。<see cref="IsIdyerin"/> が真ならイジェール文字で描く。</summary>
public readonly record struct EtymologySegment(string Text, bool IsIdyerin);

/// <summary>
/// 語源欄の記法を解析して、イジェール文字で描く部分とそれ以外に切り分ける。
///
/// 凡例（辞書の legend）が定める書式は <c>{造語者/言語略称:単語|意味}</c>。実データでは波括弧は
/// 使われず、次の形で書かれている。
///
/// <code>
///   dos+icen              合成語。語源略称が無いのでどちらもイジェール語
///   cal/mo+aker           造語者 cal（かりぐら）。語源略称は無いのでイジェール語
///   *nes+for|足の重ねる所  * は廃用語、| 以降は和訳
///   ru:Кобальт            外来語。ロシア語の綴りなのでイジェール文字にはしない
///   a:&gt;i.t:               アンコルシェ語からティタウィーニ方言へ
///   nudi&gt;                 語形変化
/// </code>
///
/// 区切りは <c>+</c>（合成）と <c>&gt;</c>（変化）。区間ごとに
/// 「造語者/」「言語略称:」を剥がし、<c>|</c> 以降は意味（和訳）として扱う。
/// 言語略称が無いか <c>i</c> で始まる（i / i.a / i.s / i.k / i.t など）ときだけ、
/// 残った語幹をイジェール語とみなす。
/// </summary>
public static class Etymology
{
    /// <summary>区間の区切り。合成の + と、変化を表す &gt;。</summary>
    private static readonly char[] Separators = { '+', '>' };

    /// <summary>
    /// イジェール語の綴りに使う文字。ここに含まれる文字が連続する範囲だけをイジェール文字にする。
    /// 語源欄には和訳や引用符が紛れ込んでいることがあり、言語略称だけを見て区間ごと切り替えると
    /// 仮名や記号までイジェール文字にしてしまうため、文字種でもう一段絞っている。
    /// </summary>
    private static bool IsIdyerinLetter(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '\'' || c == '-';

    /// <summary>
    /// 語源欄のテキストを、描き分けの単位に分割する。区間の並びは元のテキストと 1 文字も違わない
    /// （連結すれば入力に戻る）ので、呼び出し側は Run をそのまま並べればよい。
    /// </summary>
    public static IReadOnlyList<EtymologySegment> Split(string? text)
    {
        var result = new List<EtymologySegment>();
        if (string.IsNullOrEmpty(text)) return result;

        int pos = 0;
        while (pos < text.Length)
        {
            int end = text.IndexOfAny(Separators, pos);
            if (end < 0) end = text.Length;
            AppendSegment(result, text, pos, end);
            // 区切り文字そのものはイジェール文字にしない。
            if (end < text.Length) Append(result, text.Substring(end, 1), false);
            pos = end + 1;
        }

        return Merge(result);
    }

    /// <summary>1 区間（区切り文字を含まない範囲）を解析して足す。</summary>
    private static void AppendSegment(List<EtymologySegment> result, string text, int start, int end)
    {
        if (start >= end) return;
        int pos = start;

        // 「造語者/」。実データでは cal（かりぐら）だけ。ラテン文字のまま出す。
        int slash = IndexOf(text, start, end, '/');
        if (slash >= 0 && IsAsciiLetters(text, start, slash))
        {
            Append(result, text[start..(slash + 1)], false);
            pos = slash + 1;
        }

        // 「言語略称:」。イジェール語系（i, i.a, i.s, i.k, i.t …）かどうかをここで決める。
        bool idyerin = true;
        int colon = IndexOf(text, pos, end, ':');
        if (colon >= 0 && IsLanguageCode(text, pos, colon))
        {
            idyerin = IsIdyerinCode(text[pos..colon]);
            Append(result, text[pos..(colon + 1)], false);
            pos = colon + 1;
        }

        // 「|意味」以降は和訳。語形変化の「|原義>|変化後」も > で区間が割れるので同じ扱いで足りる。
        int bar = IndexOf(text, pos, end, '|');
        int etymonEnd = bar < 0 ? end : bar;

        if (idyerin) AppendIdyerin(result, text, pos, etymonEnd);
        else Append(result, text[pos..etymonEnd], false);

        if (bar >= 0) Append(result, text[bar..end], false);
    }

    /// <summary>イジェール語とみなした範囲を、綴りに使う文字の連なりだけイジェール文字にして足す。</summary>
    private static void AppendIdyerin(List<EtymologySegment> result, string text, int start, int end)
    {
        int pos = start;
        while (pos < end)
        {
            bool letter = IsIdyerinLetter(text[pos]);
            int run = pos;
            while (run < end && IsIdyerinLetter(text[run]) == letter) run++;
            Append(result, text[pos..run], letter);
            pos = run;
        }
    }

    private static void Append(List<EtymologySegment> result, string text, bool idyerin)
    {
        if (text.Length > 0) result.Add(new EtymologySegment(text, idyerin));
    }

    /// <summary>隣り合う同じ種別の区間をまとめる。Run の数を減らすだけで表示は変わらない。</summary>
    private static IReadOnlyList<EtymologySegment> Merge(List<EtymologySegment> segments)
    {
        var merged = new List<EtymologySegment>(segments.Count);
        var buffer = new StringBuilder();
        bool idyerin = false;

        foreach (var s in segments)
        {
            if (buffer.Length > 0 && s.IsIdyerin != idyerin)
            {
                merged.Add(new EtymologySegment(buffer.ToString(), idyerin));
                buffer.Clear();
            }
            idyerin = s.IsIdyerin;
            buffer.Append(s.Text);
        }
        if (buffer.Length > 0) merged.Add(new EtymologySegment(buffer.ToString(), idyerin));
        return merged;
    }

    // ---- 補助 --------------------------------------------------------------------

    private static int IndexOf(string text, int start, int end, char c)
    {
        int i = text.IndexOf(c, start);
        return i >= 0 && i < end ? i : -1;
    }

    private static bool IsAsciiLetters(string text, int start, int end)
    {
        if (start >= end) return false;
        for (int i = start; i < end; i++)
            if (!char.IsAsciiLetter(text[i])) return false;
        return true;
    }

    /// <summary>言語略称は「英小文字（と .）だけの短い綴り」。i.a のような下位区分も 1 つの略称。</summary>
    private static bool IsLanguageCode(string text, int start, int end)
    {
        if (start >= end || end - start > 4) return false;
        if (!char.IsAsciiLetter(text[start])) return false;
        for (int i = start; i < end; i++)
            if (!char.IsAsciiLetter(text[i]) && text[i] != '.') return false;
        return true;
    }

    /// <summary>
    /// イジェール語系の略称かどうか。凡例は i / i.a / i.s / i.k / i.t を挙げるが、実データには
    /// i.r・i.o もあるため「i で始まり、続きが無いか . 区切りの下位区分」であれば同じ扱いにする。
    /// 他の略称（r, a, u, en, ru, de, zh, jp …）はどれも i で始まらないので取り違えは起きない。
    /// </summary>
    private static bool IsIdyerinCode(string code) =>
        code.Length > 0 &&
        (code[0] == 'i' || code[0] == 'I') &&
        (code.Length == 1 || code[1] == '.');
}
