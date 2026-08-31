using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ZasDictWin.Models;
using ZasDictWin.Services;

namespace ZasDictWin.ViewModels;

/// <summary>
/// 例文の一覧。文と訳で絞り込み、行を押すと編集オーバーレイに移る。
/// 一覧と編集は別オーバーレイに分ける（オーバーレイ層は 1 枚しか出せないため）。
/// </summary>
public sealed class ExamplesViewModel : OverlayViewModel
{
    private readonly OtmDocument _doc;
    private string _query;

    public ExamplesViewModel(OtmDocument doc, string query = "")
    {
        _doc = doc;
        _query = query;
        Title = "例文";

        AddCommand = new RelayCommand(() => AddRequested?.Invoke());
        EditCommand = new RelayCommand(o => { if (o is Example e) EditRequested?.Invoke(e); });

        Refresh();
    }

    /// <summary>［＋ 例文］。MainViewModel が編集オーバーレイを開く。</summary>
    public Action? AddRequested { get; set; }

    /// <summary>行を押したとき。MainViewModel が編集オーバーレイを開く。</summary>
    public Action<Example>? EditRequested { get; set; }

    public ObservableCollection<Example> Examples { get; } = new();

    public string Query
    {
        get => _query;
        set { if (Set(ref _query, value)) Refresh(); }
    }

    public string CountLabel => $"{Examples.Count} / {_doc.Examples.Count} 文";

    public bool IsEmpty => Examples.Count == 0;

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }

    /// <summary>絞り込みを引き直す。例文を足した・消したあとにも呼ぶ。</summary>
    public void Refresh()
    {
        _doc.ResolveExampleForms();
        Examples.Clear();
        var q = Query.Trim();
        foreach (var e in _doc.Examples)
        {
            if (q.Length == 0 || Matches(e, q)) Examples.Add(e);
        }
        Raise(nameof(CountLabel));
        Raise(nameof(IsEmpty));
    }

    /// <summary>文・訳・補足・タグを大文字小文字を無視して部分一致で見る。</summary>
    private static bool Matches(Example e, string query) =>
        e.Sentence.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        e.Translation.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        e.Supplement.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        e.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// 例文エディタ。文が必須、それ以外は任意。出典が「自作」以外なら ZpDIC Online に番号で照会して
/// 訳と補足を引ける。APIキーは <see cref="ZpdicApi.ApiKeyPath"/> に置き、設定ファイルには混ぜない。
/// </summary>
public sealed class ExampleEditViewModel : OverlayViewModel
{
    private readonly OtmDocument _doc;
    private readonly SearchService _search;
    private readonly Action<ExampleEditViewModel> _commit;

    private string _sentence = "";
    private string _translation = "";
    private string _supplement = "";
    private string _tagsText = "";
    private string _wordQuery = "";
    private string _catalog = Const.ExampleCatalogSelf;
    private string _offerNumberText = "0";
    private string _offerStatus = "";
    private string _validationMessage = "";
    private string _apiKeyInput = "";
    private bool _isFetching;

    public ExampleEditViewModel(Example? source, OtmDocument doc, SearchService search,
                                Action<ExampleEditViewModel> commit)
    {
        _doc = doc;
        _search = search;
        _commit = commit;

        Source = source;
        Title = source is null ? "例文を追加" : "例文を編集";

        if (source is not null)
        {
            _sentence = source.Sentence;
            _translation = source.Translation;
            _supplement = source.Supplement;
            _tagsText = string.Join(", ", source.Tags);
            _catalog = source.OfferCatalog.Length == 0 ? Const.ExampleCatalogSelf : source.OfferCatalog;
            _offerNumberText = source.OfferNumber.ToString();
            foreach (var w in source.Words) Words.Add(new ExampleWord { Id = w.Id, Form = w.Form });
        }

        AddWordCommand = new RelayCommand(o => { if (o is Word w) AddWord(w); });
        RemoveWordCommand = new RelayCommand(o => { if (o is ExampleWord w) Words.Remove(w); });
        FetchCommand = new RelayCommand(async () => await FetchAsync(), () => CanFetch);
        SaveApiKeyCommand = new RelayCommand(SaveApiKey, () => ApiKeyInput.Trim().Length > 0);
        SaveCommand = new RelayCommand(() => { if (Validate()) _commit(this); });
        DeleteCommand = new RelayCommand(() => DeleteRequested?.Invoke(), () => Source is not null);
        CancelCommand = new RelayCommand(() => CancelRequested?.Invoke());
    }

    /// <summary>編集元。null なら新規。</summary>
    public Example? Source { get; }

    /// <summary>［削除］。MainViewModel が確認オーバーレイを出す。</summary>
    public Action? DeleteRequested { get; set; }

    /// <summary>［キャンセル］。MainViewModel が一覧に戻す。</summary>
    public Action? CancelRequested { get; set; }

    public string IdLabel => Source is null ? "ID: —（保存時に採番）" : $"ID: {Source.Id}";

    public string Sentence { get => _sentence; set => Set(ref _sentence, value); }
    public string TranslationText { get => _translation; set => Set(ref _translation, value); }
    public string Supplement { get => _supplement; set => Set(ref _supplement, value); }
    public string TagsText { get => _tagsText; set => Set(ref _tagsText, value); }

    public ObservableCollection<ExampleWord> Words { get; } = new();
    public ObservableCollection<Word> WordCandidates { get; } = new();

    public string WordQuery
    {
        get => _wordQuery;
        set { if (Set(ref _wordQuery, value)) RefreshCandidates(); }
    }

    /// <summary>出典プルダウンに並べる一覧（choices.json の ExampleCatalogs）。</summary>
    public IReadOnlyList<ExampleCatalog> Catalogs { get; } = Choices.Current.ExampleCatalogs;

    /// <summary>選択中の出典カタログ（API 名）。保存されるのはこの値。</summary>
    public string Catalog
    {
        get => _catalog;
        set
        {
            if (!Set(ref _catalog, value)) return;
            OfferStatus = "";
            Raise(nameof(CanFetch));
            Raise(nameof(IsOnlineCatalog));
            Raise(nameof(SelectedCatalog));
        }
    }

    /// <summary>プルダウン用。Catalog は API 名だけを持つので、選択肢の実体はここで引き当てる。</summary>
    public ExampleCatalog? SelectedCatalog
    {
        get => Catalogs.FirstOrDefault(c => c.Api == Catalog);
        set { if (value is not null) Catalog = value.Api; }
    }

    /// <summary>「自作」以外＝ZpDIC に照会できる出典。</summary>
    public bool IsOnlineCatalog => Catalog != Const.ExampleCatalogSelf;

    public string OfferNumberText
    {
        get => _offerNumberText;
        set { if (Set(ref _offerNumberText, value)) Raise(nameof(CanFetch)); }
    }

    public string OfferStatus { get => _offerStatus; private set => Set(ref _offerStatus, value); }

    public string ValidationMessage { get => _validationMessage; private set => Set(ref _validationMessage, value); }

    public bool IsFetching
    {
        get => _isFetching;
        private set { if (Set(ref _isFetching, value)) Raise(nameof(CanFetch)); }
    }

    public bool CanFetch => IsOnlineCatalog && !IsFetching && ParseOfferNumber() > 0;

    /// <summary>APIキーの入力欄。保存すると空に戻す（画面にキーを残さない）。</summary>
    public string ApiKeyInput
    {
        get => _apiKeyInput;
        set => Set(ref _apiKeyInput, value);
    }

    public bool HasApiKey => ZpdicApi.LoadApiKey() is not null;

    public string ApiKeyHint => HasApiKey
        ? $"APIキーは保存済みです（{ZpdicApi.ApiKeyPath}）。入れ直すと上書きします。"
        : $"照会には ZpDIC Online の APIキーが必要です。保存先: {ZpdicApi.ApiKeyPath}";

    public ICommand AddWordCommand { get; }
    public ICommand RemoveWordCommand { get; }
    public ICommand FetchCommand { get; }
    public ICommand SaveApiKeyCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CancelCommand { get; }

    // ---- 関連単語 --------------------------------------------------------

    private void RefreshCandidates()
    {
        WordCandidates.Clear();
        if (WordQuery.Length == 0) return;
        var q = WordQuery.ToLowerInvariant();
        foreach (var w in _doc.Words.Where(w => _search.Matches(w, q, SearchMode.Partial, SearchScope.Both)).Take(30))
            WordCandidates.Add(w);
    }

    private void AddWord(Word word)
    {
        if (Words.Any(w => w.Id == word.Id)) return;
        Words.Add(new ExampleWord { Id = word.Id, Form = word.DisplayForm });
    }

    // ---- 出典照会 --------------------------------------------------------

    private int ParseOfferNumber() => int.TryParse(OfferNumberText.Trim(), out var n) && n > 0 ? n : 0;

    private async Task FetchAsync()
    {
        var number = ParseOfferNumber();
        if (number <= 0) { OfferStatus = "番号を入力してください。"; return; }

        var key = ZpdicApi.LoadApiKey();
        if (key is null)
        {
            OfferStatus = "APIキーが未設定です。下の欄に入れて［キーを保存］を押してください。";
            return;
        }

        IsFetching = true;
        OfferStatus = "照会中…";
        ExampleOfferResult result;
        try
        {
            result = await ZpdicApi.FetchAsync(Catalog, number, key);
        }
        catch (Exception ex)
        {
            // コマンドは async void で走るため、ここで漏らすとアプリごと落ちる。
            ErrorLog.Write("例文の出典照会", ex);
            result = new ExampleOfferResult(false, $"照会に失敗しました: {ex.Message}");
        }
        finally
        {
            IsFetching = false;
        }

        OfferStatus = result.Message;
        if (result.Ok)
        {
            TranslationText = result.Translation;
            Supplement = result.Supplement;
            return;
        }
        // 通らなかったキーは残しておくと毎回同じ失敗を繰り返すので捨てる。
        if (result.KeyRejected) { ZpdicApi.DeleteApiKey(); Raise(nameof(HasApiKey)); Raise(nameof(ApiKeyHint)); }
        if (result.NotFound) OfferNumberText = "0";
    }

    private void SaveApiKey()
    {
        try
        {
            ZpdicApi.SaveApiKey(ApiKeyInput);
            ApiKeyInput = "";
            OfferStatus = "APIキーを保存しました。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            OfferStatus = $"APIキーを保存できませんでした: {ex.Message}";
        }
        Raise(nameof(HasApiKey));
        Raise(nameof(ApiKeyHint));
    }

    // ---- 保存 ------------------------------------------------------------

    private bool Validate()
    {
        var ok = !string.IsNullOrWhiteSpace(Sentence);
        ValidationMessage = ok ? "" : "「文」は必須です。";
        return ok;
    }

    /// <summary>入力を例文に書き戻す。新規なら呼び出し側が採番した Example を渡す。</summary>
    public void ApplyTo(Example example)
    {
        example.Sentence = Sentence.Trim();
        example.Translation = TranslationText.Trim();
        example.Supplement = Supplement.Trim();
        example.Tags = TagsText
            .Split(new[] { ',', '、', '，' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();
        example.Words = Words.Select(w => new ExampleWord { Id = w.Id, Form = w.Form }).ToList();
        example.OfferCatalog = Catalog;
        // 自作の例文は出典番号を持たないので、例文自身の id をそのまま出典番号にする（Python 版と同じ）。
        example.OfferNumber = Catalog == Const.ExampleCatalogSelf ? example.Id : ParseOfferNumber();
        example.WriteBack();
    }
}
