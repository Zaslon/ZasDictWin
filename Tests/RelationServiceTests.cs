using System.Collections.ObjectModel;
using ZasDictWin.Models;
using ZasDictWin.Services;

namespace ZasDictWin.Tests;

public class RelationServiceTests
{
    private static readonly RelationService Service = new(new Dictionary<string, string>
    {
        ["上位語"] = "下位語",
        ["下位語"] = "上位語",
        ["類義語"] = "類義語",
    });

    private static Word W(int id, string form)
    {
        var w = Word.CreateNew(id);
        w.Form = form;
        return w;
    }

    private static Relation R(string title, Word to) => new() { Title = title, Id = to.Id, Form = to.Form };

    [Fact]
    public void Counterpart_LooksUpTheMap()
    {
        Assert.Equal("下位語", Service.Counterpart("上位語"));
        Assert.Equal("類義語", Service.Counterpart("類義語"));
        Assert.Null(Service.Counterpart("未登録"));
        Assert.Equal(new[] { "上位語", "下位語", "類義語" }, Service.Titles);
    }

    [Fact]
    public void DefaultMap_IsACopyOfTheDefaults()
    {
        var map = RelationService.DefaultMap;
        map["上位語"] = "壊す";
        Assert.Equal("下位語", RelationService.DefaultMap["上位語"]);
    }

    [Fact]
    public void Added_RegistersCounterpartOnTarget()
    {
        var a = W(1, "a");
        var b = W(2, "b");
        var notified = 0;
        b.PropertyChanged += (_, _) => notified++;

        Service.ApplyRelations(new ObservableCollection<Word> { a, b }, a, new List<Relation> { R("上位語", b) });

        Assert.Equal("上位語", Assert.Single(a.Relations).Title);
        var back = Assert.Single(b.Relations);
        Assert.Equal(("下位語", 1, "a"), (back.Title, back.Id, back.Form));
        Assert.Equal(1, notified);
    }

    [Fact]
    public void Added_DoesNotDuplicateExistingCounterpart()
    {
        var a = W(1, "a");
        var b = W(2, "b");
        b.Relations.Add(R("下位語", a));

        Service.ApplyRelations(new ObservableCollection<Word> { a, b }, a, new List<Relation> { R("上位語", b) });

        Assert.Single(b.Relations);
    }

    [Fact]
    public void Added_WithoutCounterpartOrToSelfOrMissingTarget_TouchesOnlySource()
    {
        var a = W(1, "a");
        var b = W(2, "b");
        var gone = W(9, "gone");

        Service.ApplyRelations(new ObservableCollection<Word> { a, b }, a,
            new List<Relation> { R("未登録", b), R("類義語", a), R("類義語", gone) });

        Assert.Equal(3, a.Relations.Count);
        Assert.Empty(b.Relations);
    }

    [Fact]
    public void Removed_RemovesCounterpartFromTarget()
    {
        var a = W(1, "a");
        var b = W(2, "b");
        var c = W(3, "c");
        var all = new ObservableCollection<Word> { a, b, c };
        Service.ApplyRelations(all, a, new List<Relation> { R("上位語", b), R("類義語", c) });

        Service.ApplyRelations(all, a, new List<Relation> { R("類義語", c) });

        Assert.Empty(b.Relations);
        Assert.Single(c.Relations);
    }

    [Fact]
    public void ChangedTitle_ReplacesCounterpart()
    {
        var a = W(1, "a");
        var b = W(2, "b");
        var all = new ObservableCollection<Word> { a, b };
        Service.ApplyRelations(all, a, new List<Relation> { R("上位語", b) });

        Service.ApplyRelations(all, a, new List<Relation> { R("類義語", b) });

        Assert.Equal("類義語", Assert.Single(b.Relations).Title);
    }

    [Fact]
    public void PropagateFormChange_UpdatesOnlyRelationsPointingAtTheWord()
    {
        var a = W(1, "a");
        var b = W(2, "b");
        var c = W(3, "c");
        b.Relations.Add(R("類義語", a));
        c.Relations.Add(R("類義語", b));
        var bNotified = 0;
        var cNotified = 0;
        b.PropertyChanged += (_, _) => bNotified++;
        c.PropertyChanged += (_, _) => cNotified++;

        a.Form = "renamed";
        RelationService.PropagateFormChange(new ObservableCollection<Word> { a, b, c }, a);

        Assert.Equal("renamed", b.Relations[0].Form);
        Assert.Equal("b", c.Relations[0].Form);
        Assert.Equal((1, 0), (bNotified, cNotified));
    }

    [Fact]
    public void RemoveReferences_DropsEveryRelationToTheWord()
    {
        var a = W(1, "a");
        var b = W(2, "b");
        var c = W(3, "c");
        b.Relations.Add(R("類義語", a));
        b.Relations.Add(R("上位語", a));
        b.Relations.Add(R("類義語", c));

        RelationService.RemoveReferences(new ObservableCollection<Word> { a, b, c }, a);

        Assert.Equal(3, Assert.Single(b.Relations).Id);
    }
}
