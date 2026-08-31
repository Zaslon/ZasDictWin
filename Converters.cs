using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ZasDictWin.Views;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is true;
        if (parameter as string == "invert") b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>enum の現在値と ConverterParameter を比較する。chip 型トグルの選択表示に使う。</summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter as string;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isNull = value is null || (value is string s && s.Length == 0);
        if (parameter as string == "invert") isNull = !isNull;
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var n = value is int i ? i : (value as System.Collections.ICollection)?.Count ?? 0;
        var hasItems = n > 0;
        if (parameter as string == "invert") hasItems = !hasItems;
        return hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>2 つのバインディング値が等しいかを返す。値同士を比べる chip の選択表示に使う。</summary>
public sealed class EqualsMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        => values.Length >= 2 && Equals(values[0]?.ToString(), values[1]?.ToString());

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// ConverterParameter に書いた基準サイズ（scale=1.0 のときの px 値）に、バインドされた
/// 文字サイズ倍率を掛けて FontSize を返す。DataContext に依存せず {x:Static
/// vm:FontScaleState.Instance} 経由で使うことを想定している。
/// </summary>
public sealed class ScaleFontSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var scale = value is double d ? d : 1.0;
        var baseSize = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? b : 12.0;
        return baseSize * scale;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// ドッキング中はカードの最大幅を外す。ConverterParameter は中央表示のときの上限（px）。
/// 上下にドッキングしたときに幅だけ中途半端に絞られて、浮いたカードのように見えるのを防ぐ。
/// </summary>
public sealed class DockMaxWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true) return double.PositiveInfinity;
        return parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
            ? w
            : double.PositiveInfinity;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>ドロップ先の升目の濃さ。今カーソルが乗っている升だけ濃くする。</summary>
public sealed class DockZoneOpacityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
        => values.Length >= 2 && values[0] is not null && Equals(values[0], values[1]) ? 0.34 : 0.09;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>ヘッダとフッタの実高さを Thickness にする。窓全体に敷いた層を本文の範囲だけに収めるために使う。</summary>
public sealed class EdgeInsetsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var top = values.Length > 0 && values[0] is double t ? t : 0;
        var bottom = values.Length > 1 && values[1] is double b ? b : 0;
        return new Thickness(0, top, 0, bottom);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>設定に書かれた色文字列をそのままブラシにする。不正値は透明扱い。</summary>
public sealed class StringToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || s.Length == 0) return Brushes.Transparent;
        try { return (Brush)new BrushConverter().ConvertFromString(s)!; }
        catch (FormatException) { return Brushes.Transparent; }
        catch (NotSupportedException) { return Brushes.Transparent; }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
