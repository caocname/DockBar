using System;
using System.IO;
using Microsoft.Win32;

namespace DockBar.Services;

/// <summary>
/// 开机自启:写注册表 HKCU\...\Run,普通权限即可,启动也不弹窗。
/// 需求 3.3。
/// </summary>
internal static class AutoStart
{
    private const string KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string NAME = "DockBar";

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(KEY);
            return k?.GetValue(NAME) is string;
        }
    }

    public static void SetEnabled(bool enable)
    {
        using var k = Registry.CurrentUser.OpenSubKey(KEY, writable: true)
                     ?? Registry.CurrentUser.CreateSubKey(KEY)!;
        if (enable)
        {
            var exe = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exe)) return;
            // --silent 让用户配开机自启时不要弹主窗口
            k.SetValue(NAME, $"\"{exe}\" --silent");
        }
        else
        {
            k.DeleteValue(NAME, throwOnMissingValue: false);
        }
    }
}
