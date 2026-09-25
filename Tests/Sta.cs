using System.Runtime.ExceptionServices;

namespace ZasDictWin.Tests;

/// <summary>WPF の要素は STA スレッドでしか作れないので、xUnit の既定（MTA）から載せ替えて走らせる。</summary>
internal static class Sta
{
    public static void Run(Action body)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
