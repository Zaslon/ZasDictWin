using System.Collections.ObjectModel;
using System.Text.Json;
using ZasDictWin.Models;
using ZasDictWin.Resources;
using ZasDictWin.Services;

namespace ZasDictWin.ViewModels;

public sealed class TranslationRow : ViewModelBase
{
    private string _title = "";
    private string _formsText = "";
    public string Title { get => _title; set => Set(ref _title, value); }
    /// <summary>「犬, 狗」のようにカンマ区切りで編集し、確定時に forms 配列へ戻す。</summary>
    public string FormsText { get => _formsText; set => Set(ref _formsText, value); }
}

public sealed class ContentRow : ViewModelBase
{
    private string _title = "";
    private string _text = "";
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Text { get => _text; set => Set(ref _text, value); }
}

public sealed class VariationRow : ViewModelBase
{
    private string _title = "";
    private string _form = "";
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Form { get => _form; set => Set(ref _form, value); }
}

public sealed class RelationRow : ViewModelBase
{
    private string _title = "";
    public string Title { get => _title; set => Set(ref _title, value); }
    public int Id { get; init; }
    public string Form { get; init; } = "";
    public string CounterpartHint { get; set; } = "";
}

/// <summary>
/// 単語エディタの入力欄の入れ物。保存（辞書への書き込み・関係の対照登録・更新履歴）は AppMediator が行い、
/// ここは書きかけの行の増減と、保存前の検証・差分の有無の判定だけを持つ。
/// </summary>
public sealed class WordEditViewModel : OverlayViewModel
{
    private readonly ObservableCollection<Word> _allWords;
    private readonly RelationService _relations;
    private readonly SearchService _search;

    private string _form = "";
    private string _relationQuery = "";
    // 入力欄の初期値であり、そのまま辞書データの関連語の見出しとして保存される。
    // 区間ごとのフォント指定は効かないうえ、タグが辞書ファイルに残ってしまうので落とす。
    private string _relationTitle = EnTag.Strip(Strings.Relation_DefaultTitle);
    private string _validationMessage = "";

    /// <summary>読み込み直後（何も打っていない状態）の内容。<see cref="HasChanges"/> の基準にする。</summary>
    private readonly string _initialSnapshot;

    public WordEditViewModel(Word? source, ObservableCollection<Word> allWords, RelationService relations,
                             SearchService search, string initialForm = "")
    {
        _allWords = allWords;
        _relations = relations;
        _search = search;

        Source = source;
        Title = source is null ? Strings.WordEdit_AddTitle : Strings.WordEdit_EditTitle;

        if (source is not null)
        {
            Form = source.Form;
            TagsText = string.Join(", ", source.Tags);
            foreach (var t in source.Translations)
                Translations.Add(new TranslationRow { Title = t.Title, FormsText = string.Join(", ", t.Forms) });
            // 語法→文化→用例→語源→（その他の既存タイトル）の順で初期表示する
            foreach (var c in source.Contents.OrderBy(c => ContentRank(c.Title)))
                Contents.Add(new ContentRow { Title = c.Title, Text = c.Text });
            foreach (var v in source.Variations)
                Variations.Add(new VariationRow { Title = v.Title, Form = v.Form });
            foreach (var r in source.Relations)
                Relations.Add(new RelationRow { Title = r.Title, Id = r.Id, Form = r.Form, CounterpartHint = HintFor(r.Title) });
        }
        else
        {
            Form = initialForm.Trim();
            Translations.Add(new TranslationRow());
        }

        RelationTitles = new ObservableCollection<string>(relations.Titles);
        RefreshAvailableContentTypes();

        _initialSnapshot = Snapshot();
    }

    public Word? Source { get; }

    public string Form
    {
        get => _form;
        set => Set(ref _form, value);
    }

    /// <summary>保存時の検証エラーメッセージ（未入力なら折りたたみ表示）。</summary>
    public string ValidationMessage
    {
        get => _validationMessage;
        set => Set(ref _validationMessage, value);
    }

    /// <summary>訳語は品詞の選択が必須。何らかの内容がある行（訳語または品詞が入力済み）に、
    /// 選択肢にある品詞が選ばれていない場合は保存を止める。完全に空の行は無視して保存する。</summary>
    internal bool Validate()
    {
        var missing = Translations.Any(t =>
            (!string.IsNullOrWhiteSpace(t.FormsText) || !string.IsNullOrWhiteSpace(t.Title)) &&
            !PosTitles.Contains(t.Title.Trim()));
        ValidationMessage = missing
            ? Strings.WordEdit_ValidationPos
            : "";
        return !missing;
    }

    /// <summary>読み込み直後から中身が変わったか。閉じずに別の単語へ差し替える前の確認に使う
    /// （Translations 等は行の増減も含めて Build* 経由で比べる必要があるため、フィールドの単純比較では拾えない）。</summary>
    public bool HasChanges => Snapshot() != _initialSnapshot;

    private string Snapshot() => JsonSerializer.Serialize(new
    {
        Form = Form.Trim(),
        Tags = BuildTags(),
        Translations = BuildTranslations(),
        Contents = BuildContents(),
        Variations = BuildVariations(),
        Relations = BuildRelations(),
    });

    public string TagsText { get; set; } = "";

    public ObservableCollection<TranslationRow> Translations { get; } = new();
    public ObservableCollection<ContentRow> Contents { get; } = new();
    public ObservableCollection<VariationRow> Variations { get; } = new();
    public ObservableCollection<RelationRow> Relations { get; } = new();

    /// <summary>訳語の品詞プルダウンに並べる語彙（choices.json の Pos）。</summary>
    public IReadOnlyList<string> PosTitles { get; } = Choices.Current.Pos;

    /// <summary>まだ追加していない内容欄の種類。追加済みの種類はここから消える。</summary>
    public ObservableCollection<string> AvailableContentTypes { get; } = new();

    public ObservableCollection<string> RelationTitles { get; }
    public ObservableCollection<Word> RelationCandidates { get; } = new();

    public string RelationTitle
    {
        get => _relationTitle;
        set { if (Set(ref _relationTitle, value)) Raise(nameof(RelationTitleHint)); }
    }

    public string RelationTitleHint => HintFor(RelationTitle) is { Length: > 0 } h
        ? string.Format(Strings.WordEdit_ReciprocalHint, h)
        : Strings.WordEdit_NoReciprocalHint;

    public string RelationQuery
    {
        get => _relationQuery;
        set { if (Set(ref _relationQuery, value)) RefreshCandidates(); }
    }

    private string HintFor(string title) => _relations.Counterpart(title) ?? "";

    // ---- 書きかけの行の増減（辞書へは保存まで書かない）-----------------------

    internal void AddTranslationRow() => Translations.Add(new TranslationRow());

    internal void RemoveTranslationRow(TranslationRow row) => Translations.Remove(row);

    internal void RemoveContentRow(ContentRow row)
    {
        Contents.Remove(row);
        RefreshAvailableContentTypes();
    }

    internal void AddVariationRow() => Variations.Add(new VariationRow());

    internal void RemoveVariationRow(VariationRow row) => Variations.Remove(row);

    internal void RemoveRelationRow(RelationRow row) => Relations.Remove(row);

    /// <summary>choices.json の ContentTypes に書いた順。未知のタイトルは末尾に回す。</summary>
    private static int ContentRank(string title)
    {
        var types = Choices.Current.ContentTypes;
        var i = types.IndexOf(title);
        return i < 0 ? types.Count : i;
    }

    internal void AddContentType(string title)
    {
        if (Contents.Any(c => c.Title == title)) return;
        Contents.Add(new ContentRow { Title = title });
        var sorted = Contents.OrderBy(c => ContentRank(c.Title)).ToList();
        Contents.Clear();
        foreach (var c in sorted) Contents.Add(c);
        RefreshAvailableContentTypes();
    }

    private void RefreshAvailableContentTypes()
    {
        AvailableContentTypes.Clear();
        foreach (var t in Choices.Current.ContentTypes.Where(t => !Contents.Any(c => c.Title == t)))
            AvailableContentTypes.Add(t);
    }

    private void RefreshCandidates()
    {
        RelationCandidates.Clear();
        if (RelationQuery.Length == 0) return;
        var q = RelationQuery.ToLowerInvariant();
        foreach (var w in _allWords.Where(w => w != Source && _search.Matches(w, q, SearchMode.Partial, SearchScope.Both)).Take(30))
            RelationCandidates.Add(w);
    }

    internal void AddRelation(Word target)
    {
        if (Relations.Any(r => r.Id == target.Id && r.Title == RelationTitle)) return;
        Relations.Add(new RelationRow
        {
            Title = RelationTitle,
            Id = target.Id,
            Form = target.Form,
            CounterpartHint = HintFor(RelationTitle)
        });
        // 追加できたことが見えるよう、候補一覧（検索欄）は選択のたびに畳む。
        RelationQuery = "";
    }

    public List<Translation> BuildTranslations() => Translations
        .Where(t => !string.IsNullOrWhiteSpace(t.FormsText) || !string.IsNullOrWhiteSpace(t.Title))
        .Select(t => new Translation
        {
            Title = t.Title.Trim(),
            Forms = SplitList(t.FormsText)
        }).ToList();

    public List<string> BuildTags()
    {
        var tags = SplitList(TagsText);
        // 発音記号は内容欄の「発音記号」が単一の保存先。そこにテキストがあるかどうかで
        // 「特殊発音」タグを同期させる（自動付与／解除）。
        var hasPron = Contents.Any(c => c.Title.Trim() == Const.PronContentTitle && !string.IsNullOrWhiteSpace(c.Text));
        if (hasPron)
        {
            if (!tags.Contains(Const.SpecialPronTag)) tags.Add(Const.SpecialPronTag);
        }
        else tags.RemoveAll(t => t == Const.SpecialPronTag);
        return tags;
    }

    public List<ContentItem> BuildContents() => Contents
        .Where(c => !string.IsNullOrWhiteSpace(c.Title) || !string.IsNullOrWhiteSpace(c.Text))
        .Select(c => new ContentItem { Title = c.Title.Trim(), Text = c.Text }).ToList();

    public List<Variation> BuildVariations() => Variations
        .Where(v => !string.IsNullOrWhiteSpace(v.Form))
        .Select(v => new Variation { Title = v.Title.Trim(), Form = v.Form.Trim() }).ToList();

    public List<Relation> BuildRelations() => Relations
        .Select(r => new Relation { Title = r.Title, Id = r.Id, Form = r.Form }).ToList();

    private static List<string> SplitList(string text) => text
        .Split(new[] { ',', '、', '，' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(s => s.Trim())
        .Where(s => s.Length > 0)
        .ToList();
}
