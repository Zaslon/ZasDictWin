using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZasDictWin.Services;

/// <summary>辞書の編集元。<see cref="GitHub"/> のときは MainViewModel が自動保存を強制的に無効化する
/// （唯一の書き込みタイミングをコミットに一本化するため）。</summary>
public enum EditMode { Local, GitHub }

public sealed class AppSettings
{
    public string? LastDictionaryPath { get; set; }
    public string? ChangelogPath { get; set; }

    // 標準の枠が無いので OS は大きさを覚えてくれない。最後に閉じたときのウィンドウの大きさを自前で持つ。
    // 最大化して閉じた場合は、次に元へ戻したときの大きさが分かるよう最大化前の大きさを保つ。
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }

    public double FontScale { get; set; } = 1.0;
    public bool AutoSave { get; set; }

    public EditMode Mode { get; set; } = EditMode.Local;

    // GitHub モードの接続先。アクセストークンは秘密情報のため settings.json には含めず、
    // GitHubApi.TokenPath（%APPDATA%\ZasDictWin\github_token）に別置きする。
    public string? GitHubOwner { get; set; }
    public string? GitHubRepo { get; set; }
    public string GitHubBranch { get; set; } = "main";
    public string? GitHubJsonPath { get; set; }
    public string? GitHubChangelogPath { get; set; }

    public bool HeksaEnabled { get; set; }
    public string? HeksaFontPath { get; set; }

    public string SortOrder { get; set; } = TextProcessor.DefaultSortOrder;

    // 画面の割り付け。枠の入れ子と、枠ごとに住む種類名（OverlayViewModel.Kind）を丸ごと持つ。
    // 読めないときは既定の割り付け（左に検索、右に単語詳細）から始める。
    public DockNodeSettings? Layout { get; set; }

    // 本体の窓から持ち出したタブ（独立ウィンドウ）。窓ごとに割り付けと位置・大きさを持つ。
    // 中身が空の窓は開かないが、次にその種類を開いたとき同じ位置へ出すために記憶としては残す。
    public List<DockFloatSettings> Floats { get; set; } = new();

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
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // Mode を数値ではなく "Local" / "GitHub" のまま書く。settings.json は手で直すこともあるため。
        Converters = { new JsonStringEnumConverter() }
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

/// <summary>
/// 独立ウィンドウ 1 枚を settings.json に写したもの。中身は本体と同じ枠の入れ子で、
/// それに窓の位置と大きさが付く。位置が null のときはまだ決めていない（本体の中央に出す）。
/// メモリ上（<see cref="ViewModels.DockFloat.Bounds"/>）は決めていない位置を Rect の NaN で表すが、
/// NaN は JSON にできないため、ファイル上はここで null に変換して持つ。
/// </summary>
public sealed class DockFloatSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 620;
    public double Height { get; set; } = 520;

    /// <summary>窓の中の割り付け。枠ひとつだけのことも、割った入れ子のこともある。</summary>
    public DockNodeSettings? Node { get; set; }
}
