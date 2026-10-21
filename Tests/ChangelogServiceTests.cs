using System.IO;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Tests;

public sealed class ChangelogServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZasDictWinTests_" + Guid.NewGuid().ToString("N"));

    public ChangelogServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private string Csv => Path.Combine(_dir, "d_changelog.csv");

    private static ChangeEntry Entry(string op, string form, string detail = "")
        => new(new DateTime(2026, 10, 17, 9, 30, 0), op, form, detail);

    [Fact]
    public void DefaultPathFor_SitsNextToTheDictionary()
        => Assert.Equal(Path.Combine("C:\\dicts", "idyer_changelog.csv"), ChangelogService.DefaultPathFor(Path.Combine("C:\\dicts", "idyer.json")));

    [Fact]
    public void Append_NewFile_WritesBomAndHeaderOnce()
    {
        ChangelogService.Append(Csv, new[] { Entry("ADD", "a") });
        ChangelogService.Append(Csv, new[] { Entry("DELETE", "b") });

        var bytes = File.ReadAllBytes(Csv);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal(1, bytes.Count(b => b == 0xEF));   // 追記側は BOM を足さない
        Assert.Equal(new[] { "timestamp,type,form,details", "2026-10-17,ADD,a,", "2026-10-17,DELETE,b," },
            File.ReadAllLines(Csv));
    }

    [Fact]
    public void Append_NoEntries_DoesNotCreateFile()
    {
        ChangelogService.Append(Csv, Array.Empty<ChangeEntry>());
        Assert.False(File.Exists(Csv));
    }

    [Fact]
    public void CommasAndQuotes_RoundTrip()
    {
        ChangelogService.Append(Csv, new[] { Entry("CHANGE", "a,b", "旧: \"x\"") });

        var rows = ChangelogService.Read(Csv);

        Assert.Equal(new[] { "2026-10-17", "CHANGE", "a,b", "旧: \"x\"" }, rows[1]);
    }

    [Fact]
    public void Read_MissingFile_IsEmpty() => Assert.Empty(ChangelogService.Read(Csv));

    [Fact]
    public void Read_StopsAfterHeaderAndMaxRows()
    {
        ChangelogService.Append(Csv, Enumerable.Range(0, 10).Select(i => Entry("ADD", $"w{i}")));
        Assert.Equal(4, ChangelogService.Read(Csv, maxRows: 3).Count);
    }

    [Theory]
    [InlineData(new[] { "timestamp", "type" }, true)]
    [InlineData(new string[0], true)]
    [InlineData(new[] { "2026-10-17", "ADD" }, false)]
    [InlineData(new[] { "2026/10/17", "ADD" }, false)]
    [InlineData(new[] { "*2026-10-17", "ADD" }, false)]   // 未フラッシュの行
    [InlineData(new[] { "2026", "ADD" }, true)]
    public void IsHeaderRow_IsAnythingNotStartingWithADate(string[] cells, bool expected)
        => Assert.Equal(expected, ChangelogService.IsHeaderRow(cells));

    // ---- 更新履歴の画面 -------------------------------------------------------------

    [Fact]
    public void ViewModel_MovesHeaderRowOutOfTheBody()
    {
        var vm = new ChangelogViewModel(new[] { new[] { "日時", "種別" }, new[] { "2026-10-17", "ADD", "a", "" } }, "x.csv");

        Assert.Equal(new[] { "日時", "種別", "", "" }, vm.ChangelogHeader);   // 足りない列は空で 4 列にそろえる
        Assert.Equal("ADD", Assert.Single(vm.ChangelogRows)[1]);
    }

    [Fact]
    public void ViewModel_WithoutHeaderRow_UsesDefaultHeader()
    {
        var vm = new ChangelogViewModel(new[] { new[] { "2026-10-17", "ADD", "a", "" } }, "x.csv");

        Assert.Equal(ChangelogService.DefaultHeader, vm.ChangelogHeader);
        Assert.Single(vm.ChangelogRows);
        Assert.NotSame(ChangelogService.DefaultHeader, vm.ChangelogHeader);
    }

    [Fact]
    public void ViewModel_ReloadSwitchesPathAndRows()
    {
        var vm = new ChangelogViewModel(Array.Empty<string[]>(), "old.csv");
        vm.Reload(new[] { new[] { "2026-10-17", "ADD", "a", "" } }, "new.csv");
        Assert.Equal("new.csv", vm.ChangelogPath);
        Assert.Single(vm.ChangelogRows);
    }
}
