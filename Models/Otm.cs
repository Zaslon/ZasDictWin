using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json.Nodes;

namespace ZasDictWin.Models;

public sealed class Translation
{
    public string Title { get; set; } = "";
    public List<string> Forms { get; set; } = new();

    /// <summary>訳語綴りを表示用にまとめたもの。編集欄と同じくカンマ区切り（JSON の forms 配列は変えない）。</summary>
    public string FormSummary => string.Join(", ", Forms);
}

public sealed class ContentItem
{
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class Variation
{
    public string Title { get; set; } = "";
    public string Form { get; set; } = "";
}

public sealed class Relation
{
    public string Title { get; set; } = "";
    public int Id { get; set; }
    public string Form { get; set; } = "";
}

/// <summary>
/// OTM-JSON の 1 単語。
/// </summary>
public sealed class Word : INotifyPropertyChanged
{
    /// <summary>
    /// 読み込み時の JSON をそのまま保持する。zpdicOnline 由来のメタ情報など
    /// このアプリが解釈しないキーを保存時に失わないための土台。
    /// </summary>
    public JsonObject Raw { get; }

    public int Id { get; set; }
    public string Form { get; set; } = "";
    public List<Translation> Translations { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public List<ContentItem> Contents { get; set; } = new();
    public List<Variation> Variations { get; set; } = new();
    public List<Relation> Relations { get; set; } = new();

    /// <summary>同音異義語の連番。1 なら単独。表示専用で JSON には書き戻さない。</summary>
    public int HomonymIndex { get; set; } = 1;

    public string DisplayForm => HomonymIndex <= 1 ? Form : $"{Form} ({HomonymIndex})";

    public string TranslationSummary
    {
        get
        {
            var parts = Translations
                .Where(t => t.Forms.Count > 0)
                .Select(t => string.IsNullOrEmpty(t.Title)
                    ? t.FormSummary
                    : $"［{t.Title}］{t.FormSummary}");
            return string.Join("  ", parts);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>全バインディングを再評価させる。編集確定後に呼ぶ。</summary>
    public void NotifyChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    private Word(JsonObject raw) => Raw = raw;

    public static Word FromJson(JsonObject raw)
    {
        var w = new Word(raw);
        if (raw["entry"] is JsonObject entry)
        {
            w.Id = entry["id"]?.GetValue<int>() ?? 0;
            w.Form = entry["form"]?.GetValue<string>() ?? "";
        }

        if (raw["translations"] is JsonArray ts)
        {
            foreach (var t in ts.OfType<JsonObject>())
            {
                w.Translations.Add(new Translation
                {
                    Title = t["title"]?.GetValue<string>() ?? "",
                    Forms = (t["forms"] as JsonArray)?.Select(f => f?.GetValue<string>() ?? "").ToList() ?? new()
                });
            }
        }

        if (raw["tags"] is JsonArray tags)
            w.Tags = tags.Select(t => t?.GetValue<string>() ?? "").ToList();

        if (raw["contents"] is JsonArray cs)
        {
            foreach (var c in cs.OfType<JsonObject>())
            {
                w.Contents.Add(new ContentItem
                {
                    Title = c["title"]?.GetValue<string>() ?? "",
                    Text = c["text"]?.GetValue<string>() ?? ""
                });
            }
        }

        if (raw["variations"] is JsonArray vs)
        {
            foreach (var v in vs.OfType<JsonObject>())
            {
                w.Variations.Add(new Variation
                {
                    Title = v["title"]?.GetValue<string>() ?? "",
                    Form = v["form"]?.GetValue<string>() ?? ""
                });
            }
        }

        if (raw["relations"] is JsonArray rs)
        {
            foreach (var r in rs.OfType<JsonObject>())
            {
                var e = r["entry"] as JsonObject;
                w.Relations.Add(new Relation
                {
                    Title = r["title"]?.GetValue<string>() ?? "",
                    Id = e?["id"]?.GetValue<int>() ?? 0,
                    Form = e?["form"]?.GetValue<string>() ?? ""
                });
            }
        }

        return w;
    }

    public static Word CreateNew(int id) => FromJson(new JsonObject
    {
        ["entry"] = new JsonObject { ["id"] = id, ["form"] = "" },
        ["translations"] = new JsonArray(),
        ["tags"] = new JsonArray(),
        ["contents"] = new JsonArray(),
        ["variations"] = new JsonArray(),
        ["relations"] = new JsonArray()
    });

    public Word Duplicate(int newId)
    {
        var copy = FromJson(Raw.DeepClone().AsObject());
        copy.Id = newId;
        // 複製元の関係をそのまま持たせると相手側と非対称になるため落とす。
        copy.Relations.Clear();
        copy.WriteBack();
        return copy;
    }

    /// <summary>編集結果を Raw に反映する。Raw の未知のキーは触らない。</summary>
    public void WriteBack()
    {
        if (Raw["entry"] is JsonObject entry)
        {
            entry["id"] = Id;
            entry["form"] = Form;
        }
        else
        {
            Raw["entry"] = new JsonObject { ["id"] = Id, ["form"] = Form };
        }

        Raw["translations"] = new JsonArray(Translations.Select(t => (JsonNode)new JsonObject
        {
            ["title"] = t.Title,
            ["forms"] = new JsonArray(t.Forms.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray())
        }).ToArray());

        Raw["tags"] = new JsonArray(Tags.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray());

        Raw["contents"] = new JsonArray(Contents.Select(c => (JsonNode)new JsonObject
        {
            ["title"] = c.Title,
            ["text"] = c.Text
        }).ToArray());

        Raw["variations"] = new JsonArray(Variations.Select(v => (JsonNode)new JsonObject
        {
            ["title"] = v.Title,
            ["form"] = v.Form
        }).ToArray());

        Raw["relations"] = new JsonArray(Relations.Select(r => (JsonNode)new JsonObject
        {
            ["title"] = r.Title,
            ["entry"] = new JsonObject { ["id"] = r.Id, ["form"] = r.Form }
        }).ToArray());
    }
}

/// <summary>辞書ファイル 1 つ分。</summary>
public sealed class OtmDocument
{
    public JsonObject Root { get; }
    public ObservableCollection<Word> Words { get; }
    public string? Path { get; set; }

    public ObservableCollection<Example> Examples { get; }

    public OtmDocument(JsonObject root, IEnumerable<Word> words, string? path,
                       IEnumerable<Example>? examples = null)
    {
        Root = root;
        Words = new ObservableCollection<Word>(words);
        Examples = new ObservableCollection<Example>(examples ?? Array.Empty<Example>());
        Path = path;
        ResolveExampleForms();
    }

    /// <summary>例文が指す単語の見出し語を引き直す。単語の追加・改名・削除のあとに呼ぶ。</summary>
    public void ResolveExampleForms()
    {
        if (Examples.Count == 0) return;
        // 同じ id の単語が二重にある壊れた辞書でも落ちないよう、先勝ちで辞書を組む。
        var byId = new Dictionary<int, Word>();
        foreach (var w in Words) byId.TryAdd(w.Id, w);
        foreach (var e in Examples) e.ResolveForms(byId);
    }

    public string Name => Path is null ? "（無題）" : System.IO.Path.GetFileNameWithoutExtension(Path);

    public JsonObject ZpdicOnline
    {
        get
        {
            if (Root["zpdicOnline"] is JsonObject z) return z;
            var created = new JsonObject();
            Root["zpdicOnline"] = created;
            return created;
        }
    }

    public JsonNode? Legend => Root["legend"];

    public int NextId() => Words.Count == 0 ? 1 : Words.Max(w => w.Id) + 1;

    public int NextExampleId() => Examples.Count == 0 ? 1 : Examples.Max(e => e.Id) + 1;

    /// <summary>指定した単語を参照している例文。詳細欄の「参照例文」に出す。</summary>
    public IEnumerable<Example> ExamplesFor(int wordId) => Examples.Where(e => e.References(wordId));
}
