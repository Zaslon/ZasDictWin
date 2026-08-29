using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ZasDictWin.Models;

namespace ZasDictWin.Services;

public static class OtmJsonIo
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        // 非 ASCII をエスケープすると差分が読めなくなり、ZpDIC Online の出力とも食い違う。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = false };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public static OtmDocument Load(string path)
    {
        var text = File.ReadAllText(path);
        var node = JsonNode.Parse(text, NodeOptions, DocumentOptions)
                   ?? throw new InvalidDataException("JSON の解析結果が空です。");
        if (node is not JsonObject root)
            throw new InvalidDataException("OTM-JSON のトップレベルがオブジェクトではありません。");

        var words = new List<Word>();
        if (root["words"] is JsonArray arr)
        {
            foreach (var item in arr.OfType<JsonObject>())
                words.Add(Word.FromJson(item.DeepClone().AsObject()));
        }

        // words は Word 側が保持するので、保存時に必ず組み直す。
        root.Remove("words");
        return new OtmDocument(root, words, path);
    }

    public static OtmDocument CreateEmpty() => new(new JsonObject(), Array.Empty<Word>(), null);

    public static void Save(OtmDocument doc, string path)
    {
        foreach (var w in doc.Words) w.WriteBack();

        var root = doc.Root;
        root["words"] = new JsonArray(doc.Words.Select(w => (JsonNode)w.Raw.DeepClone()).ToArray());

        var json = root.ToJsonString(WriteOptions);
        root.Remove("words");

        // 書き込み中の電断でも元ファイルを壊さないよう、一時ファイル経由で差し替える。
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json, new System.Text.UTF8Encoding(false));
        if (File.Exists(path))
        {
            var bak = path + ".bak";
            File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
            try { File.Delete(bak); } catch (IOException) { /* 退避ファイルが残るだけなので握りつぶす */ }
        }
        else
        {
            File.Move(tmp, path);
        }

        doc.Path = path;
    }

    public static string PrettyPrint(JsonNode? node)
        => node is null ? "" : node.ToJsonString(WriteOptions);
}
