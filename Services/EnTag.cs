namespace ZasDictWin.Services;

/// <summary>UI 文字列（Resources/Strings.*.resx）の 1 区間。IsEn が真なら Strings.EnFont で描く。</summary>
public readonly record struct EnTagSegment(string Text, bool IsEn);

/// <summary>
/// UI 文字列に埋め込む <c>[en]…[/en]</c> を解析する。訳文の中に英字表記
/// （製品名や略語など）を混ぜたいとき、その部分だけ Strings.EnFont（Strings.en.resx の
/// Meta_UIFontFamily）で描かせるための手動タグで、Services/Etymology.cs の自動判定とは別の仕組み。
///
/// 角括弧なのは resx が XML だから。&lt;en&gt; と山括弧で書くと resx のコンパイル時に
/// 要素として解釈され、タグは黙って消えて中身だけが残る（GetString が返す時点でもう
/// タグが無いので、ここも含めどこにも手掛かりが残らない）。
///
/// 入れ子・属性は持たない。閉じタグが見つからない場合は開始タグ以降を非 en 扱いのまま残す
/// （壊れた入力でも本文を失わない）。
/// </summary>
public static class EnTag
{
    private const string OpenTag = "[en]";
    private const string CloseTag = "[/en]";

    public static IReadOnlyList<EnTagSegment> Split(string? text)
    {
        var result = new List<EnTagSegment>();
        if (string.IsNullOrEmpty(text)) return result;

        int pos = 0;
        while (pos < text.Length)
        {
            int start = text.IndexOf(OpenTag, pos, StringComparison.Ordinal);
            if (start < 0)
            {
                Append(result, text[pos..], false);
                break;
            }
            Append(result, text[pos..start], false);

            int contentStart = start + OpenTag.Length;
            int end = text.IndexOf(CloseTag, contentStart, StringComparison.Ordinal);
            if (end < 0)
            {
                Append(result, text[start..], false);
                break;
            }
            Append(result, text[contentStart..end], true);
            pos = end + CloseTag.Length;
        }
        return result;
    }

    /// <summary>タグを取り除いた素の文字列。区間ごとにフォントを変えられない出力先で使う
    /// （OS が描くタイトルバーやファイルダイアログ、辞書データ・コミットメッセージ・更新履歴 CSV の
    /// ように画面の外へ出ていく文字列）。今そのキーにタグが無くても、訳を足した誰かが後から
    /// [en] を書いた時点で生タグが漏れるので、そうした出力先は必ずここを通すこと。</summary>
    public static string Strip(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (!text.Contains(OpenTag, StringComparison.Ordinal)) return text;
        return string.Concat(Split(text).Select(segment => segment.Text));
    }

    private static void Append(List<EnTagSegment> result, string text, bool isEn)
    {
        if (text.Length > 0) result.Add(new EnTagSegment(text, isEn));
    }
}
