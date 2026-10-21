using ZasDictWin.Services;

namespace ZasDictWin.Tests;

public class IpaTests
{
    // 期待値は Ipa.Rules を上から順に手で当てたもの（元の ZasDict の lang/ipa.py と同じ表・同じ順）。
    [Theory]
    [InlineData("ˈæpəl", "ep(e|o)l")]       // 強勢記号を落とし、ə は選択肢になる
    [InlineData("ˌθɪŋk", "segk")]           // θ→s, ɪ→e, ŋ→g
    [InlineData("ʃɐ", "se")]                // ɐ は母音 e の行で先に消費され、(e|a) の行には届かない
    [InlineData("ɛkˈspɹɛs", "ekspr'es")]
    [InlineData("ʈɖɳɲ", "t'd'n'n'")]
    [InlineData("ʂʐçʝ", "s'z's'z'")]
    [InlineData("χʁ", "hg")]
    [InlineData("ɾɽβvðz", "rrvvzz")]
    [InlineData("", "")]
    public void ToSpelling(string ipa, string expected) => Assert.Equal(expected, Ipa.ToSpelling(ipa));
}
