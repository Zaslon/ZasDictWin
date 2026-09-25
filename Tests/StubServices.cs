using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using ZasDictWin.Mediator;
using ZasDictWin.Models;
using ZasDictWin.Root;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Tests;

/// <summary>ファイル・ダイアログ・通信を使わず、呼ばれた回数と渡された値だけを記録する外界。</summary>
internal sealed class StubServices : IAppServices
{
    public AppSettings Settings { get; } = new();
    public DateTime Now { get; set; } = new(2026, 9, 25, 12, 0, 0);

    public Dictionary<string, OtmDocument> Docs { get; } = new();
    public Dictionary<string, string> Files { get; } = new();
    public List<string> SavedPaths { get; } = new();
    public bool SaveFails { get; set; }
    public int SettingsSaves { get; private set; }
    public Dictionary<string, string>? SavedRelations { get; private set; }
    public string? NextOpenPath { get; set; }
    public string? NextSavePath { get; set; }
    public List<(string Csv, List<ChangeEntry> Entries)> Appended { get; } = new();
    public List<string> Logged { get; } = new();

    public bool GitHubConfigured { get; set; } = true;
    public string ChangelogRemotePath { get; set; } = "";
    public Func<string, GitHubFileResult> GetFile { get; set; } = _ => new GitHubFileResult(true, "", "{}");
    public GitHubCommitResult CommitResult { get; set; } = new(true, "");
    public List<(IReadOnlyList<GitHubFileChange> Files, string Message)> Commits { get; } = new();
    public bool HasGitHubToken { get; set; } = true;
    public bool TokenDeleted { get; private set; }

    public DocumentResult Load(string path)
    {
        if (Docs.TryGetValue(path, out var doc)) return new DocumentResult(true, doc, "");
        if (Files.ContainsKey(path)) return new DocumentResult(true, new OtmDocument(new JsonObject(), Array.Empty<Word>(), path), "");
        Logged.Add($"load {path}");
        return new DocumentResult(false, null, "not found");
    }

    public SaveResult Save(OtmDocument doc, string path)
    {
        if (SaveFails) return new SaveResult(false, "cannot write");
        SavedPaths.Add(path);
        doc.Path = path;
        return new SaveResult(true, "");
    }

    public OtmDocument CreateEmpty() => OtmJsonIo.CreateEmpty();
    public void SaveSettings() => SettingsSaves++;
    public void SaveRelations(Dictionary<string, string> relations) => SavedRelations = relations;
    public FontFamily? LoadHeksaFont(string? path) => null;
    public string? PickOpenPath(string title, string filter) => NextOpenPath;
    public string? PickSavePath(string title, string filter, string suggestedFileName) => NextSavePath;

    public bool TryGetGitHubTarget(out GitHubTarget target, out string error)
    {
        target = new GitHubTarget("o", "r", "main", "d.json", ChangelogRemotePath, "token");
        error = GitHubConfigured ? "" : "missing";
        return GitHubConfigured;
    }

    public string GitHubWorkingCopyPath(GitHubTarget target, string? docPath) => docPath ?? "gh.json";
    public Task<GitHubFileResult> GetGitHubFileAsync(GitHubTarget target, string path) => Task.FromResult(GetFile(path));

    public Task<GitHubCommitResult> CommitGitHubAsync(GitHubTarget target, IReadOnlyList<GitHubFileChange> files, string message)
    {
        Commits.Add((files, message));
        return Task.FromResult(CommitResult);
    }

    public void DeleteGitHubToken() => TokenDeleted = true;
    public void SaveGitHubToken(string token) => HasGitHubToken = true;
    public Task<ExampleOfferResult> FetchExampleOfferAsync(string catalog, int number) => Task.FromResult(new ExampleOfferResult(true, "ok", "tr", "sup"));
    public void SaveZpdicApiKey(string key) { }
    public void DeleteZpdicApiKey() { }
    public bool HasZpdicApiKey => false;
    public void AppendChangelog(string csv, IReadOnlyList<ChangeEntry> entries) => Appended.Add((csv, entries.ToList()));
    public IReadOnlyList<string[]> ReadChangelog(string csv) => Array.Empty<string[]>();
    public void CopyFile(string from, string to) { }
    public bool FileExists(string path) => Files.ContainsKey(path) || Docs.ContainsKey(path);
    public string ReadAllText(string path) => Files.TryGetValue(path, out var t) ? t : "";
    public void WriteAllText(string path, string text, bool withBom) => Files[path] = text;
    public void LogError(string where, Exception ex) => Logged.Add(where);
}

/// <summary>Mediator を 1 つ組み、出てきた副作用をすべて記録する。</summary>
internal sealed class MediatorHarness
{
    public MediatorHarness(Action<StubServices>? setup = null)
    {
        setup?.Invoke(Services);
        State = new MainViewModel(Services.Settings, () => Persists++);
        M = new AppMediator(Services, State, State.Layout) { HostLookup = id => Hosts.FirstOrDefault(h => h.HostId == id) };
        M.HostCommandIssued += HostCommands.Add;
        M.Changed += _ => ChangedCount++;
        M.ConfirmRequested += spec => Confirm = spec;
        M.DragEffectIssued += Effects.Add;
        M.Start();
    }

    public StubServices Services { get; } = new();
    public MainViewModel State { get; }
    public AppMediator M { get; }
    public List<IUiHost> Hosts { get; } = new();
    public List<HostCommand> HostCommands { get; } = new();
    public List<DragEffect> Effects { get; } = new();
    public ConfirmSpec? Confirm { get; private set; }
    public int Persists { get; private set; }
    public int ChangedCount { get; private set; }

    public StubHost Shell => (StubHost)(Hosts.FirstOrDefault(h => h.Role == HostRole.Shell) ?? AddHost(HostRole.Shell));

    public StubHost AddHost(HostRole role, Guid? id = null)
    {
        var host = new StubHost(role, new Rect(0, 0, 1000, 800), id: id);
        Hosts.Add(host);
        return host;
    }

    public void Send(IntentKind kind, object? payload = null, Action<IntentContext>? fill = null, Point? screen = null, IUiHost? from = null)
    {
        var ctx = new IntentContext { HostId = (from ?? Shell).HostId };
        fill?.Invoke(ctx);
        M.Dispatch(new Intent(kind, payload, ctx, screen));
    }

    public T? Open<T>() where T : OverlayViewModel => State.Layout.Overlays.OfType<T>().FirstOrDefault();

    public static Word NewWord(int id, string form, string translation = "t")
    {
        var w = Word.CreateNew(id);
        w.Form = form;
        w.Translations = new List<Translation> { new() { Title = "名詞", Forms = new List<string> { translation } } };
        w.WriteBack();
        return w;
    }

    /// <summary>辞書を 1 つ「開いた」状態にする（ファイルダイアログは stub が path を返す）。</summary>
    public OtmDocument OpenDictionary(params Word[] words)
    {
        var doc = new OtmDocument(new JsonObject(), words, "d.json");
        Services.Docs["d.json"] = doc;
        Services.NextOpenPath = "d.json";
        Send(IntentKind.OpenDictionaryRequested);
        return doc;
    }
}
