using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using ZasDictWin.Resources;
using ZasDictWin.Services;

namespace ZasDictWin;

public partial class App : Application
{
    // 短時間にこれ以上つづけざま例外を拾ったら「続けても同じことを繰り返す」状態とみなし、
    // 画面が固まったまま動かないより早く終わらせます。
    private const int MaxContinuations = 5;
    private static readonly TimeSpan GuardSpan = TimeSpan.FromSeconds(10);
    private static readonly List<DateTime> Recent = new();

    /// <summary>UI スレッドで握りつぶした例外。MainWindow がオーバーレイ表示のために購読します。</summary>
    public static event Action<Exception>? UiException;

    protected override void OnStartup(StartupEventArgs e)
    {
        // XAML の x:Static は各ウィンドウの InitializeComponent（＝StartupUri の MainWindow を含め
        // base.OnStartup より前には作られない）で一度だけ評価されるので、UI 文字列の言語は
        // それより前に確定させておく必要がある。MainViewModel も別途 AppSettings.Load() するが、
        // 設定ファイルを読むだけの軽い処理なので二重読みを気にしない。
        ApplyLanguage(AppSettings.Load().Language);

        // 既定の MessageBox は別 HWND なので OBS に映りません。ここでは記録と継続判断だけを行い、
        // 目に見える案内は MainWindow 側のオーバーレイに任せます。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ErrorLog.Write("AppDomain", args.ExceptionObject as Exception
                ?? new Exception(args.ExceptionObject?.ToString() ?? "不明な障害"));

        base.OnStartup(e);   // StartupUri の MainWindow はここで生成されるので登録は先に済ませる
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLog.Write("Dispatcher", e.Exception);
        if (!AllowContinuation()) return;

        e.Handled = true;
        UiException?.Invoke(e.Exception);
    }

    private static bool AllowContinuation()
    {
        var now = DateTime.UtcNow;
        Recent.RemoveAll(t => now - t > GuardSpan);
        if (Recent.Count >= MaxContinuations) return false;

        Recent.Add(now);
        return true;
    }

    /// <summary>UI 文字列の言語を確定させる。CurrentCulture（日付・数値の書式）には触れない。
    /// Strings.Get の参照先切り替えは Strings.SetLanguage が担う（Idyerin のような ICU に無い
    /// 自作言語コードでは CultureInfo ベースの解決ができないため。Strings.cs の説明を参照）。
    /// CurrentUICulture / DefaultThreadCurrentUICulture 自体は EllipsisMiddle の文字整形など
    /// 文字列引き当て以外でも参照されるので、こちらは従来どおり合わせておく
    /// （未知のコードでも CultureInfo の生成自体は例外にならないので安全）。
    /// 対応言語の一覧は Languages.All（Resources/Languages.cs）を参照。</summary>
    private static void ApplyLanguage(string language)
    {
        var code = Languages.Normalize(language);
        Strings.SetLanguage(code);

        var culture = new CultureInfo(code);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}

