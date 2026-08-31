using System.Text.Json.Nodes;

namespace ZasDictWin.Models;

/// <summary>例文が指している単語。form は表示用で、辞書の単語から引き直す（JSON には id だけ残す）。</summary>
public sealed class ExampleWord
{
    public int Id { get; init; }
    public string Form { get; set; } = "";
}

/// <summary>
/// 辞書ルート直下の <c>examples</c> 配列の 1 要素。ZasDict（Python 版）が書く形と同じ。
///
/// <code>
/// { "id": 3, "sentence": "…", "translation": "…", "supplement": "…",
///   "tags": ["挨拶"], "words": [{ "id": 12 }],
///   "offer": { "catalog": "自作", "number": 3 } }
/// </code>
///
/// 単語（<see cref="Word"/>）と同じく、読み込んだ JSON をそのまま抱えて未知のキーを保全する。
/// </summary>
public sealed class Example
{
    /// <summary>読み込み時の JSON。このアプリが解釈しないキーを保存時に失わないための土台。</summary>
    public JsonObject Raw { get; }

    public int Id { get; set; }
    public string Sentence { get; set; } = "";
    public string Translation { get; set; } = "";
    public string Supplement { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public List<ExampleWord> Words { get; set; } = new();

    /// <summary>出典の例文集。既定は「自作」（<see cref="Services.Const.ExampleCatalogSelf"/>）。</summary>
    public string OfferCatalog { get; set; } = "";

    /// <summary>出典の例文番号。自作なら例文自身の id と同じ値を入れる。</summary>
    public int OfferNumber { get; set; }

    /// <summary>一覧に出す 1 行目。長い例文は 50 文字で切る（Python 版と同じ）。</summary>
    public string SentencePreview => Preview(Sentence);

    /// <summary>一覧に出す 2 行目。</summary>
    public string TranslationPreview => Preview(Translation);

    public string TagSummary => string.Join(", ", Tags);

    private static string Preview(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= 50 ? line : line[..50] + "…";
    }

    private Example(JsonObject raw) => Raw = raw;

    public static Example FromJson(JsonObject raw)
    {
        var e = new Example(raw)
        {
            Id = raw["id"]?.GetValue<int>() ?? 0,
            Sentence = raw["sentence"]?.GetValue<string>() ?? "",
            Translation = raw["translation"]?.GetValue<string>() ?? "",
            Supplement = raw["supplement"]?.GetValue<string>() ?? ""
        };

        if (raw["tags"] is JsonArray tags)
            e.Tags = tags.Select(t => t?.GetValue<string>() ?? "").Where(t => t.Length > 0).ToList();

        if (raw["words"] is JsonArray words)
        {
            foreach (var w in words.OfType<JsonObject>())
            {
                if (w["id"] is { } id) e.Words.Add(new ExampleWord { Id = id.GetValue<int>() });
            }
        }

        if (raw["offer"] is JsonObject offer)
        {
            e.OfferCatalog = offer["catalog"]?.GetValue<string>() ?? "";
            e.OfferNumber = offer["number"]?.GetValue<int>() ?? 0;
        }

        return e;
    }

    public static Example CreateNew(int id) => FromJson(new JsonObject
    {
        ["id"] = id,
        ["sentence"] = "",
        ["translation"] = "",
        ["supplement"] = "",
        ["tags"] = new JsonArray(),
        ["words"] = new JsonArray(),
        ["offer"] = new JsonObject { ["catalog"] = "", ["number"] = 0 }
    });

    /// <summary>編集結果を Raw に反映する。Raw の未知のキーは触らない。</summary>
    public void WriteBack()
    {
        Raw["id"] = Id;
        Raw["sentence"] = Sentence;
        Raw["translation"] = Translation;
        Raw["supplement"] = Supplement;
        Raw["tags"] = new JsonArray(Tags.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray());
        // words は id だけ書く。form は辞書側が正で、見出し語を変えても例文の側は直さなくてよい。
        Raw["words"] = new JsonArray(Words.Select(w => (JsonNode)new JsonObject { ["id"] = w.Id }).ToArray());
        Raw["offer"] = new JsonObject { ["catalog"] = OfferCatalog, ["number"] = OfferNumber };
    }

    /// <summary>words の form を辞書の現在の見出し語で埋め直す。見つからない id は「id:12」と出す。</summary>
    public void ResolveForms(IReadOnlyDictionary<int, Word> byId)
    {
        foreach (var w in Words)
            w.Form = byId.TryGetValue(w.Id, out var word) ? word.DisplayForm : $"id:{w.Id}";
    }

    public bool References(int wordId) => Words.Any(w => w.Id == wordId);
}
