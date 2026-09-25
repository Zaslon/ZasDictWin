// ファイルダイアログを開くメソッドと通信するメソッドはテストしない（利用者の操作と外部サービスが要るため）。
using System.IO;
using ZasDictWin.Mediator;
using ZasDictWin.Services;

namespace ZasDictWin.Tests;

public sealed class AppServicesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZasDictWinTests_" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _logged = new();

    public AppServicesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private AppServices Services(AppSettings? settings = null, string? token = "t0ken")
        => new(settings ?? new AppSettings(), () => token, (where, _) => _logged.Add(where));

    [Fact]
    public void Load_ExistingFile_Succeeds()
    {
        var path = Path.Combine(_dir, "dict.json");
        File.WriteAllText(path, """{"words":[{"entry":{"id":1,"form":"a"},"translations":[],"tags":[],"contents":[],"variations":[],"relations":[]}]}""");

        var result = Services().Load(path);

        Assert.True(result.Ok);
        Assert.NotNull(result.Document);
        Assert.Single(result.Document!.Words);
    }

    [Fact]
    public void Load_MissingFile_FailsWithoutThrowing()
    {
        var result = Services().Load(Path.Combine(_dir, "nope.json"));

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrEmpty(result.Message));
        Assert.Single(_logged);
    }

    [Fact]
    public void Save_ToUnwritablePath_Fails()
    {
        var services = Services();
        var result = services.Save(services.CreateEmpty(), Path.Combine(_dir, "no_such_dir", "x.json"));

        Assert.False(result.Ok);
        Assert.Single(_logged);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var services = Services();
        var path = Path.Combine(_dir, "empty.json");
        Assert.True(services.Save(services.CreateEmpty(), path).Ok);
        Assert.True(services.Load(path).Ok);
    }

    [Theory]
    [InlineData("", "repo", "dict.json")]
    [InlineData("owner", "", "dict.json")]
    [InlineData("owner", "repo", "")]
    public void GitHubTarget_MissingRepoSettings_Fails(string owner, string repo, string json)
    {
        var settings = new AppSettings { GitHubOwner = owner, GitHubRepo = repo, GitHubJsonPath = json };

        Assert.False(Services(settings).TryGetGitHubTarget(out _, out var error));
        Assert.Equal(Resources.Strings.GitHub_ConfigMissingRepo, error);
    }

    [Fact]
    public void GitHubTarget_MissingToken_FailsWithOtherMessage()
    {
        var settings = new AppSettings { GitHubOwner = "o", GitHubRepo = "r", GitHubJsonPath = "d.json" };

        Assert.False(Services(settings, token: null).TryGetGitHubTarget(out _, out var error));
        Assert.Equal(Resources.Strings.GitHub_ConfigMissingToken, error);
    }

    [Fact]
    public void GitHubTarget_BlankBranch_BecomesMain()
    {
        var settings = new AppSettings { GitHubOwner = " o ", GitHubRepo = "r", GitHubJsonPath = "d.json", GitHubBranch = "  " };

        Assert.True(Services(settings).TryGetGitHubTarget(out var target, out _));
        Assert.Equal("main", target.Branch);
        Assert.Equal("o", target.Owner);
    }

    [Fact]
    public void GitHubTarget_ToString_HidesToken()
    {
        var target = new GitHubTarget("o", "r", "main", "d.json", "c.csv", "secret-token-value");
        Assert.DoesNotContain("secret-token-value", target.ToString());
    }
}
