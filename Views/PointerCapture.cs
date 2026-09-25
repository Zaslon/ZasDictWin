using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ZasDictWin.Views;

/// <summary>
/// ドラッグ中のマウスの掴み（キャプチャ）の扱い。キャプチャの取得・解放だけは WPF の入力モデル上
/// View でしか扱えないので、ドラッグのビヘイビアはここを通して掴み、判断は Intent で根へ任せる。
/// </summary>
internal static class PointerCapture
{
    private static readonly DependencyProperty ReleasingProperty = DependencyProperty.RegisterAttached(
        "Releasing", typeof(bool), typeof(PointerCapture), new PropertyMetadata(false));

    /// <summary>
    /// 自分から掴みを解く。LostMouseCapture は自分で解いた場合にも上がるので、印を立てておき
    /// 「離して確定」より先に「取りやめ」が届かないようにする。掴みは確定より先に解く必要がある
    /// （確定は木を組み替えるので、掴んだままだと解けなくなる）。
    /// </summary>
    public static void Release(UIElement element)
    {
        element.SetValue(ReleasingProperty, true);
        try
        {
            element.ReleaseMouseCapture();
        }
        finally
        {
            element.ClearValue(ReleasingProperty);
        }
    }

    /// <summary>いま自分で掴みを解いている最中か（その LostMouseCapture は取りやめではない）。</summary>
    public static bool IsReleasing(UIElement element) => (bool)element.GetValue(ReleasingProperty);

    /// <summary>画面上のポインタ位置（デバイスピクセル）。窓をまたぐ当たり判定はこの座標で行う。</summary>
    public static Point ScreenOf(UIElement element, MouseEventArgs e) => element.PointToScreen(e.GetPosition(element));

    /// <summary>
    /// 画面上の位置をデバイスピクセルから DIP へ直す。Window.Left / Top が DIP なので、
    /// 新しい窓を置く座標はこちらに合わせる（掴んでいる窓の拡大率で換算する）。
    /// </summary>
    public static Point ToDip(Visual visual, Point screen)
        => PresentationSource.FromVisual(visual) is { CompositionTarget: { } target }
            ? target.TransformFromDevice.Transform(screen)
            : screen;
}
