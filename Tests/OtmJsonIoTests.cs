using System.IO;
using System.Text.Json.Nodes;
using ZasDictWin.Models;
using ZasDictWin.Services;

namespace ZasDictWin.Tests;

public sealed class OtmJsonIoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZasDictWinTests_" + Guid.NewGuid().ToString("N"));

    public OtmJsonIoTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private string Write(string json)
    {
        var path = Path.Combine(_dir, "d.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Load_AcceptsCommentsAndTrailingCommas()
    {
        var doc = OtmJsonIo.Load(Write("""
            {
              // ZpDIC の出力には無いが、手で直した辞書に紛れ込む
              "words": [ { "entry": { "id": 1, "form": "a" } }, ],
            }
            """));

        Assert.Equal("a", Assert.Single(doc.Words).Form);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    public void Load_NonObject_Throws(string json)
        => Assert.Throws<InvalidDataException>(() => OtmJsonIo.Load(Write(json)));

    [Fact]
    public void Load_MovesWordsAndExamplesOutOfRoot()
    {
        var doc = OtmJsonIo.Load(Write("""{ "words": [], "examples": [ { "id": 1 } ], "legend": "# x" }"""));

        Assert.Null(doc.Root["words"]);
        Assert.Null(doc.Root["examples"]);
        Assert.Single(doc.Examples);
        Assert.Equal("# x", doc.Legend!.GetValue<string>());
    }

    [Fact]
    public void Save_KeepsUnknownKeysAndDoesNotEscapeJapanese()
    {
        var path = Write("""{ "zpdicOnline": { "k": 1 }, "version": "2", "words": [ { "entry": { "id": 1, "form": "a" }, "custom": "x" } ] }""");
        var doc = OtmJsonIo.Load(path);
        doc.Words[0].Translations.Add(new Translation { Title = "名詞", Forms = new List<string> { "船" } });

        OtmJsonIo.Save(doc, path);

        var text = File.ReadAllText(path);
        Assert.Contains("船", text);
        var saved = JsonNode.Parse(text)!;
        Assert.Equal("2", saved["version"]!.GetValue<string>());
        Assert.Equal(1, saved["zpdicOnline"]!["k"]!.GetValue<int>());
        Assert.Equal("x", saved["words"]![0]!["custom"]!.GetValue<string>());
        Assert.Null(doc.Root["words"]);   // 保存後も root は単語を抱えない（次の保存で二重にならない）
    }

    [Fact]
    public void Save_WritesExamplesOnlyWhenThereAreSome()
    {
        var path = Path.Combine(_dir, "new.json");
        var doc = OtmJsonIo.CreateEmpty();

        OtmJsonIo.Save(doc, path);
        Assert.Null(JsonNode.Parse(File.ReadAllText(path))!["examples"]);

        doc.Examples.Add(Example.CreateNew(1));
        OtmJsonIo.Save(doc, path);
        Assert.Single(JsonNode.Parse(File.ReadAllText(path))!["examples"]!.AsArray());
    }

    [Fact]
    public void Save_ReplacesExistingFileWithoutLeavingTemporaries()
    {
        var path = Write("""{ "words": [] }""");
        var doc = OtmJsonIo.Load(path);
        doc.Words.Add(Word.CreateNew(1));

        OtmJsonIo.Save(doc, path);

        Assert.Single(OtmJsonIo.Load(path).Words);
        Assert.Equal(new[] { "d.json" }, Directory.GetFiles(_dir).Select(Path.GetFileName));
        Assert.Equal(path, doc.Path);
        Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);   // BOM なし
    }

    [Fact]
    public void PrettyPrint_NullIsEmpty()
    {
        Assert.Equal("", OtmJsonIo.PrettyPrint(null));
        Assert.Contains("\"k\": \"値\"", OtmJsonIo.PrettyPrint(new JsonObject { ["k"] = "値" }));
    }
}
