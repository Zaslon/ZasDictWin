using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ZasDictWin.Views;

namespace ZasDictWin.Tests;

public class LegendPanelTests
{
    /// <summary>描画まで済ませた表示器。文書の取り合いは中身を組み立てた後でしか起きない。</summary>
    private static FlowDocumentScrollViewer LaidOut(FlowDocument? document = null)
    {
        var viewer = new FlowDocumentScrollViewer();
        if (document is not null) viewer.Document = document;
        Layout(viewer);
        return viewer;
    }

    private static void Layout(FrameworkElement e)
    {
        e.ApplyTemplate();
        e.Measure(new Size(400, 300));
        e.Arrange(new Rect(0, 0, 400, 300));
        e.UpdateLayout();
    }

    private static FlowDocument NewDocument() => new(new Paragraph(new Run("legend")));

    // Host が守っている WPF 側の制約。これが通らなくなったら Host の取り上げは不要になる。
    [Fact]
    public void PlainAssignment_ToSecondViewer_Throws() => Sta.Run(() =>
    {
        var document = NewDocument();
        LaidOut(document);

        var after = LaidOut();
        Assert.ThrowsAny<ArgumentException>(() =>
        {
            after.Document = document;
            Layout(after);
        });
    });

    [Fact]
    public void Host_TakesDocumentFromPreviousViewer() => Sta.Run(() =>
    {
        var document = NewDocument();
        var before = LaidOut(document);
        var after = LaidOut();

        LegendPanel.Host(after, document);
        Layout(after);

        Assert.Same(document, after.Document);
        Assert.Null(before.Document);
    });

    [Fact]
    public void Host_SameViewerTwice_KeepsDocument() => Sta.Run(() =>
    {
        var document = NewDocument();
        var viewer = LaidOut(document);

        LegendPanel.Host(viewer, document);
        Layout(viewer);

        Assert.Same(document, viewer.Document);
    });
}
