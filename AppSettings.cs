using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ZasDictWin.Services;

public enum LayoutMode { Split, Navigate }

public sealed class AppSettings
{
    public string? LastDictionaryPath { get; set; }
    public string? ChangelogPath { get; set; }

    public double FontScale { get; set; } = 1.0;
    public bool AutoSave { get; set; }

    public bool HeksaEnabled { get; set; }
    public string? HeksaFontPath { get; set; }

    public string SortOrder { get; set; } = TextProcessor.DefaultSortOrder;
    public Dictionary<string, string> ReciprocalMap { get; set; } = RelationService.DefaultMap;

    public LayoutMode Layout { get; set; } = LayoutMode.Split;

    public bool StreamWindowTopmost { get; set; } = true;
    public string StreamBackground { get; set; } = "#00B140";
    public double StreamFontScale { get; set; } = 2.2;
    public bool StreamShowTranslations { get; set; } = true;
    public bool StreamShowContents { get; set; }

    // 右サイドバーのブラウザ（WebView2）
    public bool BrowserVisible { get; set; }
    public double BrowserWidth { get; set; } = 420;
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
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // 設定が壊れていても起動は続行する。既定値で上書き保存される。
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
        catch (IOException)
        {
        }
    }
}
