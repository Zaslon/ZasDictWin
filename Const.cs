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

    /// <summary>出典が自分の辞書であることを表すカタログ名。ZpDIC の照会対象ではない。</summary>
    public const string ExampleCatalogSelf = "自作";

    /// <summary>例文の出典カタログ。Api が ZpDIC Online の exampleOffer に渡す名前、Label が表示名。
    /// 「自作」は照会先が無いので末尾に固定する（ZasDict の EXAMPLE_CATALOG_OPTIONS と同じ並び）。</summary>
    public static readonly IReadOnlyList<ExampleCatalog> ExampleCatalogs = new[]
    {
        new ExampleCatalog("zpdicDaily", "zpdicDaily — 今日の例文"),
        new ExampleCatalog("appleAlpha", "appleAlpha — リンゴを食べたい 58 文"),
        new ExampleCatalog("appleBeta", "appleBeta — リンゴを食べ足りない 57 文"),
        new ExampleCatalog("appleGamma", "appleGamma — リンゴをもっと食べたい 55 文"),
        new ExampleCatalog("survival", "survival — 今日を生き抜く実用例文"),
        new ExampleCatalog("weaving", "weaving — 手袋と辞書を編む 50 文"),
        new ExampleCatalog("shaleianAlpha", "shaleianAlpha — 今日のシャレイア語 I"),
        new ExampleCatalog("shaleianBeta", "shaleianBeta — 今日のシャレイア語 II"),
        new ExampleCatalog("meat", "meat — 古代の民族のためのお肉例文"),
        new ExampleCatalog("arithmetic", "arithmetic — 算数例文"),
        new ExampleCatalog("adposition", "adposition — 格や接置詞のための例文集"),
        new ExampleCatalog(ExampleCatalogSelf, ExampleCatalogSelf)
    };
}

/// <summary>例文の出典カタログ 1 つ。<paramref name="Api"/> が API に渡す名前。</summary>
public sealed record ExampleCatalog(string Api, string Label)
{
    public bool IsSelf => Api == Const.ExampleCatalogSelf;
}
