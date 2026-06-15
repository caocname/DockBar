using System;
using System.Windows;
using System.Windows.Interop;
using static DockBar.Native.NativeMethods;

namespace DockBar.Native;

/// <summary>
/// 给 WPF 窗口套上 Win11 系统级圆角 + 暗色标题。
/// 必须在窗口的 SourceInitialized 之后(HWND 已存在)调用。
/// 注:亚克力 / 毛玻璃支持已经移除,见 README "关键技术决策" 一节。
/// </summary>
internal static class WindowEffects
{
    /// <summary>
    /// 用 DWM 系统级 corner preference 给窗口套圆角 + 切到暗/亮色边框。
    /// 不依赖 layered window,所以 OLE 拖放也不会有透明区域漏检测的副作用。
    /// 无系统圆角(Win10 / 旧版本)时静默失败,窗口仍是直角。
    /// </summary>
    public static void ApplyRoundCorners(Window w, bool dark = true,
        DwmWindowCornerPreference preference = DwmWindowCornerPreference.Round)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;

        int useDark = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, (int)DwmWindowAttribute.DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref useDark, sizeof(int));

        int corner = (int)preference;
        DwmSetWindowAttribute(hwnd, (int)DwmWindowAttribute.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref corner, sizeof(int));
    }
}
