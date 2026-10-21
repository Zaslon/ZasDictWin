using System.IO;
using ZasDictWin.Mediator;
using ZasDictWin.Resources;
using ZasDictWin.Services;

namespace ZasDictWin.Tests;

/// <summary>%APPDATA%\ZasDictWin に置くファイル。テストごとに空の置き場を用意し、終われば元の置き場（一時フォルダ）へ戻す。</summary>
public sealed class AppDataTests : IDisposable
{
    private readonly string _saved = AppDataDir.Root;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ZasDictWinTests_" + Guid.NewGuid().ToString("N"));

    public AppDataTests()
    {
        AppDataDir.Root = _root;
        Choices.Reset();
    }

    public void Dispose()
    {
        AppDataDir.Root = _saved;
        Choices.Reset();
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    private string PathOf(string name) => Path.Combine(_root, name);

    private void Put(string name, string text)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(PathOf(name), text);
    }

    // ---- choices.json ------------------------------------------------------------

    [Fact]
    public void Choices_FirstRun_WritesDefaultsFile()
    {
        var current = Choices.Current;

        Assert.Equal(Choices.Defaults.Pos, current.Pos);
        var json = File.ReadAllText(PathOf("choices.json"));
        Assert.Contains("名詞", json);            // 日本語をエスケープせずに書く（手で直せるように）
        Assert.DoesNotContain("IsSelf", json);    // 導出値は書かない
    }

    [Fact]
    public void Choices_FirstRun_TakesRelationsFromLegacySettings()
    {
        Put("settings.json", """{ "ReciprocalMap": { "親": "子", "壊れ": 1 } }""");

        Assert.Equal(new Dictionary<string, string> { ["親"] = "子" }, Choices.Current.Relations);
    }

    [Fact]
    public void Choices_EmptiedListsAndMissingSelfCatalog_AreRestored()
    {
        Put("choices.json", """
            { "Pos": [], "ContentTypes": ["語法"], "Relations": {},
              "ExampleCatalogs": [ { "Api": "meat", "Label": "meat" } ] }
            """);

        var current = Choices.Current;

        Assert.Equal(Choices.Defaults.Pos, current.Pos);
        Assert.Equal(new[] { "語法" }, current.ContentTypes);
        Assert.Equal(Choices.Defaults.Relations, current.Relations);
        Assert.Equal(new[] { "meat", Const.ExampleCatalogSelf }, current.ExampleCatalogs.Select(c => c.Api));
    }

    [Fact]
    public void Choices_BrokenFile_IsKeptAndDefaultsAreUsed()
    {
        Put("choices.json", "{ broken");

        Assert.Equal(Choices.Defaults.Pos, Choices.Current.Pos);
        Assert.Equal("{ broken", File.ReadAllText(PathOf("choices.json")));   // 直せば次回そのまま読める
        Assert.Contains("選択肢の読み込み", File.ReadAllText(PathOf("error.log")));
    }

    [Fact]
    public void Choices_SavedChanges_AreReadBack()
    {
        Choices.Current.Pos = new List<string> { "独自" };
        Choices.Current.Save();
        Choices.Reset();

        Assert.Equal(new[] { "独自" }, Choices.Current.Pos);
    }

    [Fact]
    public void AppServices_SaveRelations_GoesToChoicesFile()
    {
        new AppServices(new AppSettings()).SaveRelations(new Dictionary<string, string> { ["甲"] = "乙" });
        Choices.Reset();

        Assert.Equal("乙", Choices.Current.Relations["甲"]);
    }

    // ---- settings.json -----------------------------------------------------------

    [Fact]
    public void Settings_Missing_LoadsDefaults()
    {
        var s = AppSettings.Load();
        Assert.Equal((Languages.Default, EditMode.Local, 1.0), (s.Language, s.Mode, s.FontScale));
    }

    [Fact]
    public void Settings_RoundTrip_WritesEnumsByName()
    {
        new AppSettings { Mode = EditMode.GitHub, FontScale = 1.5, GitHubOwner = "o" }.Save();

        Assert.Contains("\"GitHub\"", File.ReadAllText(PathOf("settings.json")));
        var loaded = AppSettings.Load();
        Assert.Equal((EditMode.GitHub, 1.5, "o"), (loaded.Mode, loaded.FontScale, loaded.GitHubOwner));
    }

    [Fact]
    public void Settings_Broken_LoadsDefaultsAndLogs()
    {
        Put("settings.json", "not json");

        Assert.Equal(1.0, AppSettings.Load().FontScale);
        Assert.Contains("設定の読み込み", File.ReadAllText(PathOf("error.log")));
    }

    // ---- error.log ---------------------------------------------------------------

    [Fact]
    public void ErrorLog_AppendsContextAndException()
    {
        ErrorLog.Write("one", new InvalidOperationException("first"));
        ErrorLog.Write("two", new InvalidOperationException("second"));

        var log = File.ReadAllText(ErrorLog.FilePath);
        Assert.Equal(PathOf("error.log"), ErrorLog.FilePath);
        Assert.True(log.IndexOf("one") < log.IndexOf("second"));
        Assert.Contains("InvalidOperationException: first", log);
    }

    [Fact]
    public void ErrorLog_UnwritablePlace_DoesNotThrow()
    {
        // 置き場と同じ名前のファイルがあるとフォルダを作れない。記録が書けなくても処理は続ける。
        Put("blocker", "");
        AppDataDir.Root = PathOf("blocker");

        ErrorLog.Write("ctx", new InvalidOperationException("x"));

        Assert.True(File.Exists(PathOf("blocker")));
    }

    // ---- 秘密情報（settings.json には混ぜない）-----------------------------------

    [Fact]
    public void GitHubToken_SaveTrimsLoadAndDelete()
    {
        Assert.Null(GitHubApi.LoadToken());

        GitHubApi.SaveToken("  tok  \n");
        Assert.Equal("tok", GitHubApi.LoadToken());
        Assert.True(new AppServices(new AppSettings()).HasGitHubToken);

        GitHubApi.DeleteToken();
        Assert.Null(GitHubApi.LoadToken());
        GitHubApi.DeleteToken();   // 無くても例外にしない
    }

    [Fact]
    public void GitHubToken_BlankFile_CountsAsMissing()
    {
        Put("github_token", "   ");
        Assert.Null(GitHubApi.LoadToken());
    }

    [Fact]
    public void ZpdicKey_SaveTrimsLoadAndDelete()
    {
        var services = new AppServices(new AppSettings());
        Assert.False(services.HasZpdicApiKey);

        services.SaveZpdicApiKey(" key ");
        Assert.Equal("key", ZpdicApi.LoadApiKey());
        Assert.True(services.HasZpdicApiKey);

        services.DeleteZpdicApiKey();
        Assert.Null(ZpdicApi.LoadApiKey());
    }

    [Fact]
    public async Task FetchExampleOffer_WithoutKey_DoesNotCallTheApi()
    {
        var result = await new AppServices(new AppSettings()).FetchExampleOfferAsync("meat", 3);
        Assert.False(result.Ok);
        Assert.Equal(Strings.Zpdic_KeyMissing, result.Message);
    }

    // ---- GitHub の作業コピー ------------------------------------------------------------

    [Fact]
    public void WorkingCopy_OpenDocumentIsUsedAsIs()
    {
        var target = new GitHubTarget("o", "r", "main", "dir/d.json", "", "t");
        Assert.Equal(@"C:\mine.json", new AppServices(new AppSettings()).GitHubWorkingCopyPath(target, @"C:\mine.json"));
    }

    [Fact]
    public void WorkingCopy_IsSeparatedPerRepositoryAndSanitized()
    {
        var target = new GitHubTarget("o", "r", "feat/x", "dir/d.json", "", "t");

        var path = new AppServices(new AppSettings()).GitHubWorkingCopyPath(target, null);

        Assert.Equal(PathOf(Path.Combine("github", "o__r__feat_x", "d.json")), path);
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
    }

    // ---- 素のファイル操作 -------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WriteAllText_BomIsOptional(bool withBom)
    {
        Directory.CreateDirectory(_root);
        var path = PathOf("x.txt");
        var services = new AppServices(new AppSettings());

        services.WriteAllText(path, "あ", withBom);

        Assert.Equal(withBom, File.ReadAllBytes(path)[0] == 0xEF);   // 「あ」の UTF-8 は 0xE3 から始まる
        Assert.Equal("あ", services.ReadAllText(path));
    }

    [Fact]
    public void CopyFile_Overwrites()
    {
        Put("a.txt", "new");
        Put("b.txt", "old");
        var services = new AppServices(new AppSettings());

        services.CopyFile(PathOf("a.txt"), PathOf("b.txt"));

        Assert.Equal("new", File.ReadAllText(PathOf("b.txt")));
        Assert.True(services.FileExists(PathOf("b.txt")));
    }
}
