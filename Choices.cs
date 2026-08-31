using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ZasDictWin.Services;

/// <summary>
/// 編集画面のプルダウンに並ぶ選択肢。`%APPDATA%\ZasDictWin\choices.json` を書き換えれば、
/// ビルドし直さずに辞書ごとの語彙へ合わせられる。初回起動時に既定値のファイルを書き出す。
///
/// settings.json とは分ける（あちらは「辞書のパスと表示設定だけ」を持つ約束）。
/// 既定値はこのプロパティ初期値で、ZasDict（Android 版）の Const.kt と同じ並び。
/// 語彙のうち処理の分岐に使う値（発音記号・語源・自作）だけは <see cref="Const"/> に残してある。
/// それらを選択肢から消しても壊れはせず、結び付いた自動処理が働かなくなるだけ。
/// </summary>
public sealed class Choices
{
    /// <summary>品詞。訳語ごとに 1 つ選ぶ（保存時の必須項目）。</summary>
    public List<string> Pos { get; set; } = new()
    {
        "名詞", "代名詞", "固有名詞", "動詞", "記述詞", "法性記述詞", "助詞",
        "接続詞", "間投詞", "慣用句", "ことわざ", "接頭辞", "接尾辞", "助動詞"
    };

    /// <summary>内容欄の種類。エディタでは書いた順に追加ボタンとして並び、各項目は 1 つまで。
    /// <see cref="Const.PronContentTitle"/> と <see cref="Const.EtymologyContentTitle"/> は
    /// それぞれ特殊発音タグの同期と語源の字形描画に結び付いている。</summary>
    public List<string> ContentTypes { get; set; } = new() { "発音記号", "語法", "文化", "用例", "語源" };

    /// <summary>関係名と、その対照になる関係名。片側を登録すると相手側にはここの値が入る。
    /// 対照が要らない関係は自分自身を書く（「類義語=類義語」）。設定画面からも編集できる。</summary>
    public Dictionary<string, string> Relations { get; set; } = new()
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

    /// <summary>例文の出典。Api が ZpDIC Online の exampleOffer に渡す名前、Label が表示名。
    /// 「自作」は照会先が無いので末尾に置く。</summary>
    public List<ExampleCatalog> ExampleCatalogs { get; set; } = new()
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
        new ExampleCatalog(Const.ExampleCatalogSelf, Const.ExampleCatalogSelf)
    };

    private static Choices? _current;

    /// <summary>読み込み済みの選択肢。初回アクセスでファイルを読む。</summary>
    public static Choices Current => _current ??= Load();

    /// <summary>手を入れる前の選択肢。設定画面の［既定に戻す］が参照する。</summary>
    public static Choices Defaults => new();

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZasDictWin");

    private static string FilePath => Path.Combine(Dir, "choices.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 保存できなくてもアプリは続行する（この回の編集内容はメモリ上では効いている）。
            ErrorLog.Write("選択肢の保存", ex);
        }
    }

    private static Choices Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<Choices>(File.ReadAllText(FilePath), Options) ?? new Choices();
                loaded.FillGaps();
                return loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 壊れたファイルは上書きせずに残す（直せば次回そのまま読める）。
            ErrorLog.Write("選択肢の読み込み", ex);
            return new Choices();
        }

        // 初回。書き換える取っ掛かりになるよう、既定値をそのまま置く。
        var fresh = new Choices();
        if (LegacyRelations() is { Count: > 0 } legacy) fresh.Relations = legacy;
        fresh.Save();
        return fresh;
    }

    /// <summary>空にした項目は既定値へ戻す。1 つも無いと編集画面から選べなくなるため。</summary>
    private void FillGaps()
    {
        var d = Defaults;
        if (Pos is not { Count: > 0 }) Pos = d.Pos;
        if (ContentTypes is not { Count: > 0 }) ContentTypes = d.ContentTypes;
        if (Relations is not { Count: > 0 }) Relations = d.Relations;
        if (ExampleCatalogs is not { Count: > 0 }) ExampleCatalogs = d.ExampleCatalogs;

        // 「自作」は ZpDIC に照会しない例文の受け皿なので、消されていても戻す。
        if (!ExampleCatalogs.Any(c => c.IsSelf))
            ExampleCatalogs.Add(new ExampleCatalog(Const.ExampleCatalogSelf, Const.ExampleCatalogSelf));
    }

    /// <summary>対照表は settings.json 側に置いていたので、choices.json が無い初回だけそこから拾う。</summary>
    private static Dictionary<string, string>? LegacyRelations()
    {
        try
        {
            var path = Path.Combine(Dir, "settings.json");
            if (!File.Exists(path)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("ReciprocalMap", out var map) ||
                map.ValueKind != JsonValueKind.Object) return null;

            return map.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.String)
                .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            ErrorLog.Write("旧対照表の引き継ぎ", ex);
            return null;
        }
    }
}
