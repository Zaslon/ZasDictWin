using System.IO;
using System.Runtime.CompilerServices;
using ZasDictWin.Services;

namespace ZasDictWin.Tests;

/// <summary>%APPDATA%\ZasDictWin を、テストを走らせるたびに作る一時フォルダへ向ける。</summary>
internal static class TestAppData
{
    // どのテストより先に差し替えないと、Choices.Current などが利用者の実ファイルを読み書きしてしまう。
    [ModuleInitializer]
    internal static void Redirect()
    {
        AppDataDir.Root = Path.Combine(Path.GetTempPath(), "ZasDictWinTests_AppData_" + Guid.NewGuid().ToString("N"));
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(AppDataDir.Root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        };
    }
}
