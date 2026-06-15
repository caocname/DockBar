using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace DockBar.Native;

/// <summary>
/// 从 .lnk / .exe / .url / .website / .appref-ms 提取高分屏可用的图标(48px / 256px)。
/// 用 SHGetFileInfo + SHIL_JUMBO 取大尺寸 HICON,转 ImageSource 后释放。
/// </summary>
internal static class IconExtractor
{
    private const uint SHGFI_SYSICONINDEX = 0x4000;
    private const uint SHIL_EXTRALARGE = 0x2; // 48px
    private const uint SHIL_JUMBO       = 0x4; // 256px

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]  public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(uint iImageList, ref Guid riid, out IImageList ppv);

    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
    }

    public static BitmapSource? Extract(string path, bool jumbo = true)
    {
        if (!File.Exists(path)) return null;
        // 缓存键 = 路径 + 修改时间 + jumbo;文件改了图标也跟着变
        var fi = new FileInfo(path);
        var key = $"{path}|{fi.LastWriteTimeUtc.Ticks}|{(jumbo ? 1 : 0)}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var src = ExtractInternal(path, jumbo);
        if (src is null) return null;
        // 限制缓存大小,LRU 简化版
        if (_cache.Count > 200) _cache.Clear();
        _cache[key] = src;
        return src;
    }

    private static readonly Dictionary<string, BitmapSource> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static void ClearCache() => _cache.Clear();

    private static BitmapSource? ExtractInternal(string path, bool jumbo)
    {
        var info = new SHFILEINFO();
        var hImg = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_SYSICONINDEX);
        if (hImg == IntPtr.Zero) return null;

        Guid iidImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");
        if (SHGetImageList(jumbo ? SHIL_JUMBO : SHIL_EXTRALARGE, ref iidImageList, out var imgList) != 0
            || imgList is null)
            return null;

        IntPtr hIcon = IntPtr.Zero;
        try
        {
            const int ILD_TRANSPARENT = 0x00000001;
            imgList.GetIcon(info.iIcon, ILD_TRANSPARENT, out hIcon);
            if (hIcon == IntPtr.Zero) return null;

            var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            if (hIcon != IntPtr.Zero) NativeMethods.DestroyIcon(hIcon);
            Marshal.ReleaseComObject(imgList);
        }
    }
}
