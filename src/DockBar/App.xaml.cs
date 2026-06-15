using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;

namespace DockBar;

public partial class App : Application
{
    private static Mutex? _singleInstance;

    /// <summary>
    /// 应用显示名,任务管理器"应用"分组、Alt+Tab、任务栏悬浮 都看这个 +
    /// 窗口 Title。多个 VoidTidy 进程同时跑时按编号区分:第 N 个 = "VoidTidy{N}"。
    /// 单实例锁(下面的 _singleInstance) 默认情况下重启不会出现 N>1,
    /// 但同时启动两个 exe 副本(不同路径)、Mutex 没拿到也会让进程继续跑出 UI 出错的极端场景下,
    /// 至少任务管理器里的名字能区分开。
    /// </summary>
    public static string DisplayName { get; private set; } = "VoidTidy";

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例锁,避免重复启动
        _singleInstance = new Mutex(true, "DockBar.SingleInstance.{B7A2D3E4-1A2B-4CDE-9F01-2233445566AA}", out var isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        // 计算当前已有几个 VoidTidy 进程(包括我们自己)。
        // ProcessName 不带 .exe,我们 csproj 里 AssemblyName=VoidTidy → exe 叫 VoidTidy.exe → 进程名 "VoidTidy"。
        // 万一有用户改了 exe 文件名,Path.GetFileNameWithoutExtension(MainModule.FileName) 走 fallback。
        try
        {
            var me = Process.GetCurrentProcess();
            var myName = me.ProcessName;
            var siblings = Process.GetProcessesByName(myName);
            int n = siblings.Length;
            DisplayName = n <= 1 ? "VoidTidy" : $"VoidTidy{n}";
        }
        catch { /* 出错就用默认 "VoidTidy" */ }

        base.OnStartup(e);

        // 加载配置 → 创建主窗口与触发条
        var cfg = Services.ConfigStore.Load();
        Services.AppHost.Current = new Services.AppHost(cfg);
        Services.AppHost.Current.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services.AppHost.Current?.Dispose();
        _singleInstance?.ReleaseMutex();
        base.OnExit(e);
    }
}
