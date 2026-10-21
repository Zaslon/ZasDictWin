using ZasDictWin.Resources;
using ZasDictWin.Services;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Tests;

// 期待値は規則を上から順に手で当てたもの。各関数は元の ZasDict の lang/dialects.py と行単位で同じ並び。
public class DialectsTests
{
    private static readonly string Or = EnTag.Strip(Strings.Dialect_Or);

    [Fact]
    public void Ortho1_LowersAllButStressAndSpellsPalatals()
        => Assert.Equal("#kyIsyiφ", Dialects.Ortho1("#KIsiφ"));

    [Fact]
    public void Ortho2_TurnsYAfterConsonantIntoApostrophe()
        => Assert.Equal("#ias'aφ", Dialects.Ortho2("#yasyaφ"));

    [Fact]
    public void Strip_DropsMarkersAndExpandsAmbiguousLetters()
        => Assert.Equal("s(a|i)(e|a)(b|u)(sa|s'i)(r'a|ri)", Dialects.Strip("#seoqxlφ"));

    [Fact]
    public void CommonE_KeepsFinalVowel_CommonF_DropsIt()
    {
        Assert.Equal("#kEteφ", Dialects.CommonE("#kAtaφ"));
        Assert.Equal("#kEtφ", Dialects.CommonF("#kAtaφ"));
        // 語頭の h は落ちる。
        Assert.Equal("#Eφ", Dialects.CommonF("#hAφ"));
    }

    [Fact]
    public void Arzafire_ShiftsVowelsAndConsonants() => Assert.Equal("#sIniφ", Dialects.Arzafire("#kAtaφ"));

    [Fact]
    public void Convert_SingleReading()
        => Assert.Equal(new DialectResult("kec", "ket", "ke", "san"), Dialects.Convert("kAt"));

    [Fact]
    public void Convert_FinalVowelGivesTwoReadings()
        => Assert.Equal(new DialectResult(
                $"kec(a|i){Or}kec", $"ket(a|i){Or}ket", $"ket(a|i){Or}ke", "sani"),
            Dialects.Convert("kAta"));

    [Fact]
    public void Titauini_ThreeVowelRule_OnlyWhenNotFaithful()
    {
        var saved = Dialects.FaithfulTitauini;
        try
        {
            Dialects.FaithfulTitauini = true;
            Assert.Equal("o", Dialects.Titauini("#Oφ"));
            Dialects.FaithfulTitauini = false;
            Assert.Equal("u", Dialects.Titauini("#Oφ"));
        }
        finally { Dialects.FaithfulTitauini = saved; }
    }

    [Fact]
    public void ToolViewModel_ConvertsAsYouType()
    {
        var saved = Dialects.FaithfulTitauini;
        try
        {
            var vm = new DialectToolViewModel();
            Assert.Equal("", vm.Sekore);

            var raised = new List<string?>();
            vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
            vm.Input = "kAt";

            Assert.Equal(("kec", "ket", "ke", "san"), (vm.Sekore, vm.Titauini, vm.Kaiko, vm.Arzafire));
            Assert.Contains(nameof(vm.Sekore), raised);

            vm.FaithfulTitauini = !saved;
            Assert.Equal(!saved, Dialects.FaithfulTitauini);

            vm.Input = "";
            Assert.Equal("", vm.Arzafire);
        }
        finally { Dialects.FaithfulTitauini = saved; }
    }

    [Fact]
    public void ToolViewModel_InitialInputIsConverted()
        => Assert.Equal("kec", new DialectToolViewModel("kAt").Sekore);
}
