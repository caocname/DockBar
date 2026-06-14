// net48 的 BCL 没有 Math.Clamp(.NET Core 2.0+ / .NET Standard 2.1 才加进 BCL)。
// 我们只用 double 重载,简单实现一份就够了,不再为此引第三方包。

namespace System;

internal static class MathPolyfill
{
    /// <summary>把 value 钳到 [min, max] 闭区间内。</summary>
    public static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
