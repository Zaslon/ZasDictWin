using System.Windows.Markup;
using System.Windows.Media;
using ZasDictWin.Resources;
using ZasDictWin.Services;

namespace ZasDictWin.ViewModels;

/// <summary>プルダウンの 1 項目。<see cref="Label"/> を表示し、<see cref="Value"/> を設定に書く。</summary>
public sealed record DisplayOption(string Label, object? Value);

/// <summary>
/// 表示の設定（表示 → 表示の設定…）。UI 倍率・文字の大きさ・書体・密度を、選んだ時点で反映する
/// （ZasDesignSystem rules.md「表示の設定を備える」）。設定ウィンドウは［適用］でまとめて反映する画面なので、
/// 反映の仕方を混ぜないよう別の画面にしてある。確認ダイアログの層に重ねるので、配信にも映る。
/// 値を変えると <see cref="Changed"/> を上げ、書き戻しと反映は AppMediator が行う。
/// </summary>
public sealed class DisplaySettingsViewModel : OverlayViewModel
{
    private double _uiScale;
    private double _fontScale;
    private Density _density;
    private string? _fontFamily;
    private IReadOnlyList<DisplayOption>? _fontOptions;

    public DisplaySettingsViewModel(AppSettings settings)
    {
        Title = Strings.Display_Title;
        _uiScale = settings.UiScale;
        _fontScale = settings.FontScale;
        _density = settings.Density;
        _fontFamily = settings.UiFontFamily;
    }

    /// <summary>どれかの値を選んだ。</summary>
    public event Action? Changed;

    public double UiScale => _uiScale;
    public double FontScale => _fontScale;
    public Density Density => _density;
    public string? FontFamilyName => _fontFamily;

    public IReadOnlyList<DisplayOption> UiScaleOptions { get; } =
        TextStyles.UiScaleChoices.Select(p => new DisplayOption($"{p}%", p / 100.0)).ToList();

    public DisplayOption? SelectedUiScale
    {
        get => UiScaleOptions.MinBy(o => Math.Abs((double)o.Value! - _uiScale));
        set
        {
            if (value?.Value is not double v || Math.Abs(v - _uiScale) < 0.001) return;
            _uiScale = v;
            Raise();
            Changed?.Invoke();
        }
    }

    /// <summary>文字の大きさの選択肢。本文の大きさ（px）で示し、密度で範囲が変わる。</summary>
    public IReadOnlyList<DisplayOption> FontSizeOptions
        => TextStyles.BodySizeChoices(_density).Select(px => new DisplayOption($"{px}px", px)).ToList();

    public DisplayOption? SelectedFontSize
    {
        get
        {
            var px = TextStyles.NearestBody(_fontScale, _density);
            return FontSizeOptions.FirstOrDefault(o => (int)o.Value! == px);
        }
        set
        {
            if (value?.Value is not int px) return;
            var scale = TextStyles.ScaleForBody(px, _density);
            if (Math.Abs(scale - _fontScale) < 0.001) return;
            _fontScale = scale;
            Raise();
            Changed?.Invoke();
        }
    }

    /// <summary>開いている間に Ctrl＋ホイールで文字の大きさが動いたとき、選択表示を追わせる。</summary>
    internal void SyncFontScale(double scale)
    {
        _fontScale = scale;
        Raise(nameof(SelectedFontSize));
    }

    public bool IsStandardDensity
    {
        get => _density == Density.Standard;
        set { if (value) SetDensity(Density.Standard); }
    }

    public bool IsCompactDensity
    {
        get => _density == Density.Compact;
        set { if (value) SetDensity(Density.Compact); }
    }

    /// <summary>密度を変えると本文の大きさの選択肢も変わるので、font-scale を新しい選択肢の上に丸め直す。</summary>
    private void SetDensity(Density density)
    {
        if (_density == density) return;
        _density = density;
        _fontScale = TextStyles.ScaleForBody(TextStyles.NearestBody(_fontScale, density), density);
        Raise(nameof(IsStandardDensity));
        Raise(nameof(IsCompactDensity));
        Raise(nameof(FontSizeOptions));
        Raise(nameof(SelectedFontSize));
        Changed?.Invoke();
    }

    /// <summary>書体の一覧。先頭は表示言語の既定の書体。一覧を作るのは重いので、プルダウンが引いた時点で作る。</summary>
    public IReadOnlyList<DisplayOption> FontOptions => _fontOptions ??= BuildFontOptions();

    public DisplayOption? SelectedFont
    {
        get => FontOptions.FirstOrDefault(o => string.Equals(o.Value as string, _fontFamily, StringComparison.OrdinalIgnoreCase))
               ?? FontOptions[0];
        set
        {
            if (value is null) return;
            var name = value.Value as string;
            if (string.Equals(name, _fontFamily, StringComparison.OrdinalIgnoreCase)) return;
            _fontFamily = name;
            Raise();
            Changed?.Invoke();
        }
    }

    private static IReadOnlyList<DisplayOption> BuildFontOptions()
    {
        var list = new List<DisplayOption>
        {
            new(string.Format(Strings.Display_FontDefault, Strings.Meta_UIFontFamily), null)
        };
        list.AddRange(UiFonts.JapaneseCapable().Select(f => new DisplayOption(f.Label, f.Source)));
        return list;
    }
}

/// <summary>本文と UI の書体の候補。</summary>
public static class UiFonts
{
    private static IReadOnlyList<(string Label, string Source)>? _cache;
    private static readonly XmlLanguage Japanese = XmlLanguage.GetLanguage("ja-jp");

    /// <summary>OS に入っている書体のうち、日本語の字形（「あ」）を持つもの。
    /// 選んだ書体に無い字形は既定の書体で出すが、日本語を丸ごと既定の書体で出す書体は選ぶ意味が無い。</summary>
    public static IReadOnlyList<(string Label, string Source)> JapaneseCapable() => _cache ??= Build();

    private static IReadOnlyList<(string Label, string Source)> Build()
    {
        var result = new List<(string Label, string Source)>();
        foreach (var family in Fonts.SystemFontFamilies)
        {
            try
            {
                var typeface = family.GetTypefaces().FirstOrDefault();
                if (typeface is null || !typeface.TryGetGlyphTypeface(out var glyphs)) continue;
                if (!glyphs.CharacterToGlyphMap.ContainsKey('あ')) continue;
                var label = family.FamilyNames.TryGetValue(Japanese, out var ja) ? ja : family.Source;
                result.Add((label, family.Source));
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // 壊れた書体や読めない書体は候補から外すだけにする。
            }
        }
        return result
            .DistinctBy(f => f.Source, StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f.Label, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>選んだ書体の後ろに既定の書体を並べる。選んだ書体に無い字形を既定の書体で出すため。</summary>
    public static FontFamily Compose(string? selected, string fallback)
        => string.IsNullOrWhiteSpace(selected) ? new FontFamily(fallback) : new FontFamily($"{selected}, {fallback}");
}
