using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ZasDictWin.Services;

public sealed class AppSettings
{
    public string? LastDictionaryPath { get; set; }
    public string? ChangelogPath { get; set; }

    public double FontScale { get; set; } = 1.0;
    public bool AutoSave { get; set; }

    public bool HeksaEnabled { get; set; }
    public string? HeksaFontPath { get; set; }

    public string SortOrder { get; set; } = TextProcessor.DefaultSortOrder;

    // 画面の割り付け。枠の入れ子と、枠ごとに住む種類名（OverlayViewModel.Kind）を丸ごと持つ。
    // 読めないときは既定の割り付け（左に検索、右に単語詳細）から始める。
    public DockNodeSettings? Layout { get; set; }

    public bool StreamWindowTopmost { get; set; } = true;
    public string StreamBackground { get; set; } = "#00B140";
    public double StreamFontScale { get; set; } = 2.2;
    public bool StreamShowTranslations { get; set; } = true;
    public bool StreamShowContents { get; set; }

    // ブラウザ（WebView2）のタブ。開いているかどうかと開始 URL だけを覚える。
    // 大きさは枠の割り付けが持つので、ここには持たない。
    public bool BrowserVisible { get; set; }
    public string BrowserStartUrl { get; set; } = "https://www.google.com/";

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZasDictWin");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 設定が壊れていても起動は続行する。既定値で上書き保存される。
            // 起動直後に既定値へ戻る理由が後から追えるよう、記録だけは残す。
            ErrorLog.Write("設定の読み込み", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 保存できなくてもアプリは続行する（次に触った設定でまた試みる）。
            // %APPDATA% が書けない環境では毎回ここに来るので、握りつぶさず記録に残す。
            ErrorLog.Write("設定の保存", ex);
        }
    }
}

/// <summary>
/// 画面の割り付けを settings.json に写したもの。<see cref="Axis"/> があれば境目の節、
/// 無ければタブ束ひとつぶんの枠を表す。枠には「そこに住む種類名」を並び順で持たせ、
/// 閉じているタブも次に開いたとき同じ枠へ出せるようにしている。
/// </summary>
public sealed class DockNodeSettings
{
    /// <summary>"Columns"（左右に並べる）か "Rows"（上下に並べる）。枠なら null。</summary>
    public string? Axis { get; set; }

    /// <summary>境目の位置。First 側の取り分。</summary>
    public double Ratio { get; set; } = 0.5;

    public DockNodeSettings? First { get; set; }
    public DockNodeSettings? Second { get; set; }

    /// <summary>枠の通し番号。種類ごとの行き先を引き当てる鍵。</summary>
    public int Id { get; set; }

    /// <summary>この枠に住む種類名。並び順がそのままタブの並び。</summary>
    public List<string> Tabs { get; set; } = new();
}
