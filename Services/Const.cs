using System.Text.Json.Serialization;

namespace ZasDictWin.Services;

/// <summary>
/// 処理の分岐に使う語彙。ZasDictAndroid の domain/Const.kt に合わせている。
/// 画面に並べるだけの選択肢は <see cref="Choices"/>（choices.json）が持つ。
/// </summary>
public static class Const
{
    /// <summary>発音記号を保存する内容欄の title。この欄の中身で「特殊発音」タグを同期する。</summary>
    public const string PronContentTitle = "発音記号";

    /// <summary>語源を保存する内容欄の title。
    /// この欄だけ表示時に <see cref="Etymology"/> で語幹を切り出し、イジェール文字で描く。</summary>
    public const string EtymologyContentTitle = "語源";

    /// <summary>内容欄の「発音記号」にテキストが入っている単語に自動で付くタグ（空になったら自動で外す）。</summary>
    public const string SpecialPronTag = "特殊発音";

    /// <summary>出典が自分の辞書であることを表すカタログ名。ZpDIC の照会対象ではない。</summary>
    public const string ExampleCatalogSelf = "自作";
}

/// <summary>例文の出典カタログ 1 つ。<paramref name="Api"/> が API に渡す名前。</summary>
public sealed record ExampleCatalog(string Api, string Label)
{
    /// <summary>choices.json に書き出す必要のない導出値。</summary>
    [JsonIgnore]
    public bool IsSelf => Api == Const.ExampleCatalogSelf;
}
