using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace DockBar.Native;

/// <summary>
/// 调用 Shell 的 IContextMenu,弹出与「资源管理器右键 lnk/exe」一模一样的原生菜单。
/// 需求 2.5.2 明确要求菜单内容完全一致。
/// </summary>
internal static class ShellContextMenu
{
    public static void Show(string filePath, IntPtr hwndOwner, int screenX, int screenY)
    {
        IntPtr pidlFull = IntPtr.Zero;
        IntPtr parentObj = IntPtr.Zero;
        IntPtr pidlChild = IntPtr.Zero;
        IntPtr ctxMenuPtr = IntPtr.Zero;
        IntPtr hMenu = IntPtr.Zero;
        try
        {
            uint sfgao;
            int hr = NativeMethods.SHParseDisplayName(filePath, IntPtr.Zero, out pidlFull, 0, out sfgao);
            if (hr != 0 || pidlFull == IntPtr.Zero) return;

            Guid IID_IShellFolder = typeof(IShellFolder).GUID;
            hr = NativeMethods.SHBindToParent(pidlFull, ref IID_IShellFolder, out parentObj, out pidlChild);
            if (hr != 0 || parentObj == IntPtr.Zero) return;

            var folder = (IShellFolder)Marshal.GetObjectForIUnknown(parentObj);

            Guid IID_IContextMenu = typeof(IContextMenu).GUID;
            IntPtr[] pidls = new[] { pidlChild };
            hr = folder.GetUIObjectOf(hwndOwner, 1, pidls, ref IID_IContextMenu, IntPtr.Zero, out ctxMenuPtr);
            if (hr != 0 || ctxMenuPtr == IntPtr.Zero) return;

            var ctxMenu = (IContextMenu)Marshal.GetObjectForIUnknown(ctxMenuPtr);

            hMenu = CreatePopupMenu();
            const uint CMF_NORMAL = 0;
            const uint CMF_EXTENDEDVERBS = 0x00000100; // Shift+右键的扩展项
            ctxMenu.QueryContextMenu(hMenu, 0, 1, 0x7FFF, CMF_NORMAL | CMF_EXTENDEDVERBS);

            const uint TPM_RETURNCMD = 0x0100;
            const uint TPM_RIGHTBUTTON = 0x0002;
            uint cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON,
                screenX, screenY, hwndOwner, IntPtr.Zero);

            if (cmd > 0)
            {
                var ici = new CMINVOKECOMMANDINFO
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                    fMask = 0,
                    hwnd = hwndOwner,
                    lpVerb = (IntPtr)(cmd - 1),
                    lpParameters = IntPtr.Zero,
                    lpDirectory = IntPtr.Zero,
                    nShow = 1
                };
                ctxMenu.InvokeCommand(ref ici);
            }
        }
        catch
        {
            // 静默失败,菜单弹不出来不影响其他功能
        }
        finally
        {
            if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
            if (ctxMenuPtr != IntPtr.Zero) Marshal.Release(ctxMenuPtr);
            if (parentObj != IntPtr.Zero) Marshal.Release(parentObj);
            if (pidlFull != IntPtr.Zero) NativeMethods.CoTaskMemFree(pidlFull);
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [StructLayout(LayoutKind.Sequential)]
    internal struct CMINVOKECOMMANDINFO
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr h, IntPtr p, [MarshalAs(UnmanagedType.LPWStr)] string pszName, out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);
        [PreserveSig] int EnumObjects(IntPtr h, int grfFlags, out IntPtr ppenumIDList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr p, ref Guid r, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr p, ref Guid r, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr p1, IntPtr p2);
        [PreserveSig] int CreateViewObject(IntPtr h, ref Guid r, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
        [PreserveSig] int SetNameOf(IntPtr h, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO pici);
        [PreserveSig] int GetCommandString(IntPtr idcmd, uint uflags, IntPtr reserved, IntPtr commandstring, int cch);
    }
}
