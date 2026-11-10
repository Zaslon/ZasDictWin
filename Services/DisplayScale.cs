namespace ZasDictWin.Services;

/// <summary>文字の密度。<see cref="Compact"/> は文字を小さくし、行の間を詰める。</summary>
public enum Density { Standard, Compact }

/// <summary>
/// ZasDesignSystem tokens.md「文字スタイル」「表示の設定」の値。
/// XAML からは ScaleFontSizeConverter の ConverterParameter にトークン名（"body" など）を書いて引く。
/// </summary>
public static class TextStyles
{
    private readonly record struct Style(double Size, double LineHeight, double CompactSize, double CompactLineHeight);

    private static readonly Dictionary<string, Style> Table = new()
    {
        ["display"] = new(48, 1.4, 36, 1.3),
        ["h1"] = new(28, 1.5, 22, 1.4),
        ["h2"] = new(20, 1.5, 18, 1.4),
        ["h3"] = new(18, 1.6, 16, 1.4),
        ["body"] = new(16, 1.7, 14, 1.5),
        ["dense"] = new(16, 1.3, 14, 1.2),
        ["oneline"] = new(16, 1.0, 14, 1.0),
        ["mono"] = new(16, 1.5, 14, 1.4),
        ["small"] = new(14, 1.3, 14, 1.2),
    };

    public const double MinScale = 1.0;
    public const double MaxScale = 2.0;

    public static bool IsToken(string name) => Table.ContainsKey(name);

    /// <summary>倍率を掛ける前の大きさ（px）。</summary>
    public static double Size(string token, Density density)
    {
        var s = Table[token];
        return density == Density.Compact ? s.CompactSize : s.Size;
    }

    /// <summary>行高（大きさに対する比）。</summary>
    public static double LineHeight(string token, Density density)
    {
        var s = Table[token];
        return density == Density.Compact ? s.CompactLineHeight : s.LineHeight;
    }

    /// <summary>
    /// 文字の大きさの選択肢を本文の大きさ（px）で返す。標準は 16〜32、詰めるときは 14〜28。
    /// ユーザは倍率より結果の大きさの方が選びやすい（tokens.md「表示の設定」）。
    /// </summary>
    public static IReadOnlyList<int> BodySizeChoices(Density density)
    {
        var body = (int)Size("body", density);
        var list = new List<int>();
        for (var px = body; px <= body * MaxScale + 0.001; px += 2) list.Add(px);
        return list;
    }

    /// <summary>本文の大きさ（px）から font-scale を求める。</summary>
    public static double ScaleForBody(int px, Density density) => Math.Clamp(px / Size("body", density), MinScale, MaxScale);

    /// <summary>font-scale を、その密度の選択肢のうち最も近い本文の大きさに丸める。
    /// 密度を切り替えると同じ倍率でも本文の大きさが選択肢から外れるため、選択肢の上に戻す。</summary>
    public static int NearestBody(double scale, Density density)
    {
        var target = Size("body", density) * scale;
        return BodySizeChoices(density).MinBy(px => Math.Abs(px - target));
    }

    /// <summary>font-scale を選択肢 1 つぶん動かす（Ctrl＋ホイール・Ctrl＋＋／－）。</summary>
    public static double StepScale(double scale, Density density, int steps)
    {
        var choices = BodySizeChoices(density);
        var index = choices.ToList().IndexOf(NearestBody(scale, density));
        var next = Math.Clamp(index + steps, 0, choices.Count - 1);
        return ScaleForBody(choices[next], density);
    }

    /// <summary>UI 倍率の選択肢（%）。</summary>
    public static IReadOnlyList<int> UiScaleChoices { get; } = new[] { 100, 125, 150, 175, 200 };
}
