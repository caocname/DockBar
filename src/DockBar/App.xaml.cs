using System;
using System.Threading;
using System.Windows;

namespace DockBar;

public partial class App : Application
{
    private static Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例锁,避免重复启动
        _singleInstance = new Mutex(true, "DockBar.SingleInstance.{B7A2D3E4-1A2B-4CDE-9F01-2233445566AA}", out var isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

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
