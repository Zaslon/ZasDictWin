using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using ZasDictWin.ViewModels;

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

    public LayoutMode Layout { get; set; } = LayoutMode.Split;

    // 編集画面などのオーバーレイをドッキングした辺と大きさ。Floating は画面中央のモーダル表示。
    public DockSide OverlayDock { get; set; } = DockSide.Floating;
    public double OverlayDockWidth { get; set; } = 520;
    public double OverlayDockHeight { get; set; } = 340;

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
