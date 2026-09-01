using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ZasDictWin.ViewModels;

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
/// 分割の下見の位置。枠の実寸と <see cref="SplitPreview"/> から、これから新しくできる側だけが
/// 残るような余白を返す。枠いっぱいに敷いた着色を、この余白で割る位置まで削って見せる。
/// </summary>
public sealed class SplitMarginConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is not double width || values[1] is not double height
            || values[2] is not SplitPreview preview)
            return new Thickness(0);

        if (preview.Axis == DockAxis.Columns)
        {
            var line = width * preview.Ratio;
            return preview.NewIsSecond ? new Thickness(line, 0, 0, 0) : new Thickness(0, 0, width - line, 0);
        }

        var y = height * preview.Ratio;
        return preview.NewIsSecond ? new Thickness(0, y, 0, 0) : new Thickness(0, 0, 0, height - y);
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
