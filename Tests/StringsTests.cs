using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using ZasDictWin.Resources;

namespace ZasDictWin.Tests;

public class StringsTests
{
    private static readonly Assembly App = typeof(Strings).Assembly;

    private static readonly PropertyInfo[] TextProperties = typeof(Strings)
        .GetProperties(BindingFlags.Public | BindingFlags.Static)
        .Where(p => p.PropertyType == typeof(string))
        .ToArray();

    private static HashSet<string> Keys(string baseName)
    {
        var set = new ResourceManager(baseName, App).GetResourceSet(CultureInfo.InvariantCulture, true, false)!;
        return set.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
    }

    public static IEnumerable<object[]> TranslatedLanguages
        => Languages.All.Where(l => l.Code != Languages.Default).Select(l => new object[] { l.Code });

    // 既定の resx にキーが無いプロパティは、黙ってキー名そのものを画面に出してしまう。
    [Fact]
    public void EveryProperty_HasAKeyInTheDefaultResx()
    {
        var keys = Keys("ZasDictWin.Resources.Strings");
        Assert.All(TextProperties, p => Assert.Contains(p.Name, keys));
    }

    // 訳の resx に綴りを間違えたキーがあっても、どこからも引かれずに黙って既定言語へ落ちる。
    [Theory]
    [MemberData(nameof(TranslatedLanguages))]
    public void EveryTranslatedKey_ExistsInTheDefaultResx(string code)
    {
        var defaults = Keys("ZasDictWin.Resources.Strings");
        Assert.All(Keys($"ZasDictWin.Resources.Strings.{code}"), k => Assert.Contains(k, defaults));
    }

    [Fact]
    public void SetLanguage_SwitchesAndFallsBack()
    {
        try
        {
            var japanese = Strings.Common_Cancel;

            Strings.SetLanguage("en");
            Assert.NotEqual(japanese, Strings.Common_Cancel);

            Strings.SetLanguage("no-such-language");
            Assert.Equal(japanese, Strings.Common_Cancel);
        }
        finally { Strings.SetLanguage(Languages.Default); }
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("idz", "idz")]
    [InlineData("fr", Languages.Default)]
    [InlineData(null, Languages.Default)]
    public void Languages_Normalize(string? code, string expected) => Assert.Equal(expected, Languages.Normalize(code));

    [Fact]
    public void DefaultLanguage_MatchesSettingsDefault()
        => Assert.Equal(Languages.Default, new Services.AppSettings().Language);
}
