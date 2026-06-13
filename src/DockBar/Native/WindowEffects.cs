using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static DockBar.Native.NativeMethods;

namespace DockBar.Native;

/// <summary>
/// 给 WPF 窗口套上 Win11 真亚克力毛玻璃 + 圆角 + 暗色标题。
/// 必须在窗口的 SourceInitialized 事件之后(HWND 已存在)调用。
/// </summary>
internal static class WindowEffects
{
    /// <param name="dark">true=暗色调,false=浅色调,直接影响毛玻璃叠加色</param>
    /// <param name="tintAlpha">叠加色不透明度 0~255。0 = 不叠加任何颜色,只保留系统高斯模糊。</param>
    public static void ApplyAcrylic(Window w, bool dark = true, byte tintAlpha = 0x00)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;

        int useDark = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, (int)DwmWindowAttribute.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));

        int corner = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, (int)DwmWindowAttribute.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        // GradientColor 是 0xAABBGGRR(注意是 BGR 不是 RGB)。
        // dark = 深灰 (0x202020) 叠在桌面上压暗
        // light = 浅灰 (0xF2F2F2) 叠在桌面上提亮
        uint rgb = dark ? 0x00202020u : 0x00F2F2F2u;
        uint color = ((uint)tintAlpha << 24) | rgb;

        var accent = new AccentPolicy
        {
            AccentState = AccentState.ENABLE_ACRYLICBLURBEHIND,
            GradientColor = color,
            AccentFlags = 2,
        };
        SetAccent(hwnd, accent);
    }

    /// <summary>触发条这种小窗口用半透明 BlurBehind 即可,亚克力开销稍大不划算。</summary>
    public static void ApplyBlur(Window w, bool dark = true, byte tintAlpha = 0x00)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        uint rgb = dark ? 0x00202020u : 0x00F2F2F2u;
        uint color = ((uint)tintAlpha << 24) | rgb;
        var accent = new AccentPolicy
        {
            AccentState = AccentState.ENABLE_BLURBEHIND,
            GradientColor = color,
            AccentFlags = 2,
        };
        SetAccent(hwnd, accent);
    }

    private static void SetAccent(IntPtr hwnd, AccentPolicy accent)
    {
        var size = Marshal.SizeOf<AccentPolicy>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = 19, // WCA_ACCENT_POLICY
                Data = ptr,
                SizeOfData = size,
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }
}

