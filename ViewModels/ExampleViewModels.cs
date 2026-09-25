using System.Collections.ObjectModel;
using ZasDictWin.Models;
using ZasDictWin.Resources;
using ZasDictWin.Services;

namespace ZasDictWin.ViewModels;

/// <summary>
/// 例文の一覧。文と訳で絞り込み、行を押すと編集オーバーレイに移る。
/// 一覧と編集は別オーバーレイなので、編集を開いても一覧は隣のタブとして残る。
/// </summary>
public sealed class ExamplesViewModel : OverlayViewModel
{
    private readonly OtmDocument _doc;
    private string _query;

    public ExamplesViewModel(OtmDocument doc, string query = "")
    {
        _doc = doc;
        _query = query;
        Title = Strings.Examples_Title;
        Refresh();
    }

    public ObservableCollection<Example> Examples { get; } = new();

    public string Query
    {
        get => _query;
        set { if (Set(ref _query, value)) Refresh(); }
    }

    public string CountLabel => string.Format(Strings.Examples_CountLabel, Examples.Count, _doc.Examples.Count);

    public bool IsEmpty => Examples.Count == 0;

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
/// 訳と補足を引ける（照会そのものは AppMediator が行い、結果をここへ書き戻す）。
/// APIキーは <see cref="ZpdicApi.ApiKeyPath"/> に置き、設定ファイルには混ぜない。
/// </summary>
public sealed class ExampleEditViewModel : OverlayViewModel
{
    private readonly OtmDocument _doc;
    private readonly SearchService _search;

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
    private bool _hasApiKey;

    public ExampleEditViewModel(Example? source, OtmDocument doc, SearchService search, bool hasApiKey)
    {
        _doc = doc;
        _search = search;
        _hasApiKey = hasApiKey;

        Source = source;
        Title = source is null ? Strings.ExampleEdit_AddTitle : Strings.ExampleEdit_EditTitle;

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
    }

    /// <summary>編集元。null なら新規。</summary>
    public Example? Source { get; }

    /// <summary>［削除］を押せるか（新規の例文には消す相手が無い）。</summary>
    public bool CanDelete => Source is not null;

    public string IdLabel => Source is null ? Strings.ExampleEdit_IdPending : string.Format(Strings.ExampleEdit_IdFormat, Source.Id);

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

    public string OfferStatus { get => _offerStatus; internal set => Set(ref _offerStatus, value); }

    public string ValidationMessage { get => _validationMessage; private set => Set(ref _validationMessage, value); }

    public bool IsFetching
    {
        get => _isFetching;
        internal set { if (Set(ref _isFetching, value)) Raise(nameof(CanFetch)); }
    }

    public bool CanFetch => IsOnlineCatalog && !IsFetching && OfferNumber > 0;

    /// <summary>出典番号の入力欄を数に直したもの。数でない・0 以下なら 0。</summary>
    public int OfferNumber => int.TryParse(OfferNumberText.Trim(), out var n) && n > 0 ? n : 0;

    /// <summary>APIキーの入力欄。保存すると空に戻す（画面にキーを残さない）。</summary>
    public string ApiKeyInput
    {
        get => _apiKeyInput;
        set { if (Set(ref _apiKeyInput, value)) Raise(nameof(CanSaveApiKey)); }
    }

    public bool CanSaveApiKey => ApiKeyInput.Trim().Length > 0;

    public bool HasApiKey
    {
        get => _hasApiKey;
        internal set { if (Set(ref _hasApiKey, value)) Raise(nameof(ApiKeyHint)); }
    }

    public string ApiKeyHint => HasApiKey
        ? string.Format(Strings.Zpdic_KeyHintSaved, ZpdicApi.ApiKeyPath)
        : string.Format(Strings.Zpdic_KeyHintMissing, ZpdicApi.ApiKeyPath);

    // ---- 関連単語 --------------------------------------------------------

    private void RefreshCandidates()
    {
        WordCandidates.Clear();
        if (WordQuery.Length == 0) return;
        var q = WordQuery.ToLowerInvariant();
        foreach (var w in _doc.Words.Where(w => _search.Matches(w, q, SearchMode.Partial, SearchScope.Both)).Take(30))
            WordCandidates.Add(w);
    }

    internal void AddWord(Word word)
    {
        if (Words.Any(w => w.Id == word.Id)) return;
        Words.Add(new ExampleWord { Id = word.Id, Form = word.DisplayForm });
        // 追加できたことが見えるよう、候補一覧（検索欄）は選択のたびに畳む。
        WordQuery = "";
    }

    internal void RemoveWord(ExampleWord word) => Words.Remove(word);

    // ---- 保存 ------------------------------------------------------------

    /// <summary>保存できる入力か。できなければ案内を出して false。</summary>
    internal bool Validate()
    {
        var ok = !string.IsNullOrWhiteSpace(Sentence);
        ValidationMessage = ok ? "" : Strings.ExampleEdit_ValidationSentence;
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
        example.OfferNumber = Catalog == Const.ExampleCatalogSelf ? example.Id : OfferNumber;
        example.WriteBack();
    }
}
