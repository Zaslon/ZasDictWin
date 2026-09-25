using System.Windows;
using ZasDictWin.ViewModels;

namespace ZasDictWin.Mediator;

/// <summary>割り付けの規則。木そのものは触らず、可否と行き先だけを決める純粋関数。</summary>
public static class LayoutRules
{
    public const int MaxDepth = 8;
    public const int MaxLeaves = 24;

    public enum PlaceTarget { RememberedLeaf, NewFloat, MainLeaf }

    /// <summary>タブの行き先。覚えている枠がまだあればそこ、無ければ独立ウィンドウ向きの種類は新しい窓、
    /// それ以外は既定の枠。記憶が消えた枠を指していても落ちずに次の候補へ進む。</summary>
    public static PlaceTarget ResolvePlacement(
        string kind, bool prefersFloating, IReadOnlyDictionary<string, int> homes,
        IReadOnlySet<int> existingLeafIds, out int rememberedLeafId)
    {
        if (homes.TryGetValue(kind, out var id) && existingLeafIds.Contains(id))
        {
            rememberedLeafId = id;
            return PlaceTarget.RememberedLeaf;
        }
        rememberedLeafId = -1;
        return prefersFloating ? PlaceTarget.NewFloat : PlaceTarget.MainLeaf;
    }

    /// <summary>外へ落とすのが意味を持つか。すでに 1 枚きりの独立ウィンドウにいるタブは、
    /// 外へ落としても同じ窓が生まれ直すだけなので動かさない。</summary>
    public static bool CanFloat(int leafCount, int sourceItemCount, bool sourceIsFloatRoot)
    {
        if (leafCount >= MaxLeaves) return false;
        return !(sourceItemCount == 1 && sourceIsFloatRoot);
    }

    public static bool CanSplit(int leafCount, Size leafSize, DockAxis axis, double minLeafSize)
    {
        if (leafCount >= MaxLeaves) return false;
        var total = axis == DockAxis.Columns ? leafSize.Width : leafSize.Height;
        return total >= minLeafSize * 2;
    }

    /// <summary>枠を畳むと窓ごと閉じることになるか（独立ウィンドウに枠が 1 つしか無い場合）。</summary>
    public static bool DissolveClosesHost(bool hasParent, bool isFloatRoot) => !hasParent && isFloatRoot;

    /// <summary>境目を引いたあとの取り分。両側に最小幅を残し、目に見えない差は動かさない。
    /// 割り付けがそもそも最小幅 2 つぶん無ければ元のまま。</summary>
    public static double ClampRatio(double ratio, double total, double minLeafSize)
        => ClampRatio(ratio, ratio, total, minLeafSize);

    /// <summary><paramref name="current"/> から <paramref name="next"/> へ動かすときの取り分。</summary>
    public static double ClampRatio(double current, double next, double total, double minLeafSize)
    {
        if (total <= minLeafSize * 2) return current;
        var margin = minLeafSize / total;
        var clamped = Math.Clamp(next, margin, 1 - margin);
        return Math.Abs(clamped - current) < 0.0005 ? current : clamped;
    }
}
