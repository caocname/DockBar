using System;
using System.Runtime.InteropServices;
using System.Text;
using static DockBar.Native.NativeMethods;

namespace DockBar.Native;

/// <summary>
/// 检测「前台窗口是否在我们的显示器上独占全屏」。
/// 需求 4:全屏游戏/视频时,触发条必须隐藏,不能遮挡。
/// </summary>
internal static class FullscreenDetector
{
    public static bool IsForegroundFullscreenOn(IntPtr ourHwnd)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        if (fg == GetDesktopWindow() || fg == GetShellWindow()) return false;
        if (fg == ourHwnd) return false;

        // 排除桌面相关窗口类
        var sb = new StringBuilder(64);
        GetClassName(fg, sb, sb.Capacity);
        var cls = sb.ToString();
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

        if (!GetWindowRect(fg, out var rect)) return false;

        var mon = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref mi)) return false;

        // 只有当前台窗口和我们触发条在同一个显示器才算「挡住我们」
        if (ourHwnd != IntPtr.Zero)
        {
            var ourMon = MonitorFromWindow(ourHwnd, MONITOR_DEFAULTTONEAREST);
            if (ourMon != mon) return false;
        }

        return rect.Left   <= mi.rcMonitor.Left
            && rect.Top    <= mi.rcMonitor.Top
            && rect.Right  >= mi.rcMonitor.Right
            && rect.Bottom >= mi.rcMonitor.Bottom;
    }
}
