namespace ZasDictWin.Services;

/// <summary>ZasDictAndroid の domain/Const.kt に合わせた語彙の定数。</summary>
public static class Const
{
    /// <summary>品詞の選択肢（訳語ごとに1つ選ぶ）。</summary>
    public static readonly IReadOnlyList<string> ValidPos = new[]
    {
        "名詞", "代名詞", "固有名詞", "動詞", "記述詞", "法性記述詞", "助詞",
        "接続詞", "間投詞", "慣用句", "ことわざ", "接頭辞", "接尾辞", "助動詞"
    };

    /// <summary>関係の選択肢。</summary>
    public static readonly IReadOnlyList<string> ValidRelations = new[]
    {
        "類義語", "対義語", "上位語", "下位語", "関連", "参照", "省略", "同意"
    };

    /// <summary>
    /// 関係の対照関係。片側を登録すると相手側にはここで対応付けた関係名が入る。
    /// ValidRelations と同じ並びを保つこと（設定画面の初期値がこの順で出る）。
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ReciprocalMap = new Dictionary<string, string>
    {
        ["類義語"] = "類義語",
        ["対義語"] = "対義語",
        ["上位語"] = "下位語",
        ["下位語"] = "上位語",
        ["関連"] = "関連",
        ["参照"] = "参照",
        ["省略"] = "省略",
        ["同意"] = "同意"
    };

    /// <summary>内容欄の種類。エディタでは上から順に追加ボタンとして表示し、各項目は1つまで。
    /// 「発音記号」が pronunciation の唯一の保存先（専用の発音欄や variations への保存は使わない）。</summary>
    public static readonly IReadOnlyList<string> ContentTypes = new[] { "発音記号", "語法", "文化", "用例", "語源" };

    /// <summary>発音記号を保存する内容欄の title。ContentTypes の先頭要素と同じ値にする。</summary>
    public const string PronContentTitle = "発音記号";

    /// <summary>内容欄の「発音記号」にテキストが入っている単語に自動で付くタグ（空になったら自動で外す）。</summary>
    public const string SpecialPronTag = "特殊発音";
}
