using System.Collections.ObjectModel;
using ZasDictWin.Models;

namespace ZasDictWin.Services;

/// <summary>
/// 関係の対照登録。片側を編集したら相手側にも自動で反映する。
/// </summary>
public sealed class RelationService
{
    private readonly IReadOnlyDictionary<string, string> _reciprocal;

    public RelationService(IReadOnlyDictionary<string, string> reciprocalMap) => _reciprocal = reciprocalMap;

    public static Dictionary<string, string> DefaultMap => new(Const.ReciprocalMap);

    public IEnumerable<string> Titles => _reciprocal.Keys;

    public string? Counterpart(string title)
        => _reciprocal.TryGetValue(title, out var t) ? t : null;

    /// <summary>
    /// source の関係を newRelations の内容に差し替え、増減分を相手側にも適用する。
    /// </summary>
    public void ApplyRelations(ObservableCollection<Word> all, Word source, List<Relation> newRelations)
    {
        var before = source.Relations;
        var added = newRelations.Where(n => !before.Any(b => b.Id == n.Id && b.Title == n.Title)).ToList();
        var removed = before.Where(b => !newRelations.Any(n => n.Id == b.Id && n.Title == b.Title)).ToList();

        source.Relations = newRelations;

        foreach (var rel in added)
        {
            var counterTitle = Counterpart(rel.Title);
            if (counterTitle is null) continue;
            var target = all.FirstOrDefault(w => w.Id == rel.Id);
            if (target is null || target == source) continue;
            if (target.Relations.Any(r => r.Id == source.Id && r.Title == counterTitle)) continue;
            target.Relations.Add(new Relation { Title = counterTitle, Id = source.Id, Form = source.Form });
            target.NotifyChanged();
        }

        foreach (var rel in removed)
        {
            var counterTitle = Counterpart(rel.Title);
            if (counterTitle is null) continue;
            var target = all.FirstOrDefault(w => w.Id == rel.Id);
            if (target is null || target == source) continue;
            var victim = target.Relations.FirstOrDefault(r => r.Id == source.Id && r.Title == counterTitle);
            if (victim is null) continue;
            target.Relations.Remove(victim);
            target.NotifyChanged();
        }
    }

    /// <summary>見出し語が変わったとき、自分を指している relations の form を更新する。</summary>
    public static void PropagateFormChange(ObservableCollection<Word> all, Word changed)
    {
        foreach (var w in all)
        {
            var touched = false;
            foreach (var r in w.Relations)
            {
                if (r.Id != changed.Id || r.Form == changed.Form) continue;
                r.Form = changed.Form;
                touched = true;
            }
            if (touched) w.NotifyChanged();
        }
    }

    /// <summary>単語削除時に、その単語を指している関係をすべて外す。</summary>
    public static void RemoveReferences(ObservableCollection<Word> all, Word removedWord)
    {
        foreach (var w in all)
        {
            var n = w.Relations.RemoveAll(r => r.Id == removedWord.Id);
            if (n > 0) w.NotifyChanged();
        }
    }
}
