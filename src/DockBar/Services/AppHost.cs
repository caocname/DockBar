using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using DockBar.Native;
using DockBar.UI;

namespace DockBar.Services;

/// <summary>
/// 全局上下文。维持 触发条 / 主窗口 / 文件监听 / 全屏检测计时器 / 托盘。
/// 注意:类名故意叫 AppHost(不叫 AppContext),避开 System.AppContext 同名冲突。
/// </summary>
public sealed class AppHost : IDisposable
{
    public static AppHost? Current { get; set; }

    public AppConfig Config { get; }
    internal FolderBinder Binder { get; }

    private TriggerWindow? _trigger;
    private MainWindow? _main;
    private TrayIcon? _tray;
    private DispatcherTimer? _fullscreenTimer;

    public AppHost(AppConfig cfg)
    {
        Config = cfg;
        Binder = new FolderBinder(Application.Current.Dispatcher);
        Binder.Bind(cfg.FolderPath);
        Binder.Changed += () => _main?.RefreshItems();
    }

    public void Start()
    {
        _trigger = new TriggerWindow();
        _main = new MainWindow();
        _tray = new TrayIcon(this);

        _trigger.Show();
        _trigger.ApplyDock();
        // 主窗口初始 Visibility=Collapsed,不会创建 HWND;
        // 但 OLE 拖放目标必须在 HWND 注册才生效,等到第一次 ShowMain 才建会跟 drag session 抢跑,
        // 表现就是「拖到主窗口面板上没反应」。这里强制建 HWND,SourceInitialized 立刻跑(DWM 圆角 + UIPI 过滤),
        // 同时 WPF 把 AllowDrop=True 的窗口注册成 OLE drop target,后续任何拖拽都能命中。
        new System.Windows.Interop.WindowInteropHelper(_main).EnsureHandle();
        _main.ApplyDock();

        // 250ms 检查:① 全屏检测 ② 鼠标移出窗口的兜底隐藏(因 WPF MouseLeave 在 ToolTip/拖拽时不可靠)
        _fullscreenTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _fullscreenTimer.Tick += (_, _) => OnTick();
        _fullscreenTimer.Start();

        // 启动 5 秒后做一次首次内存 trim,把启动期 JIT 残留还给系统
        var firstTrim = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        firstTrim.Tick += (s, _) =>
        {
            ((DispatcherTimer)s!).Stop();
            TrimWorkingSet();
        };
        firstTrim.Start();

        // 首次启动若未绑定文件夹,提示用户去设置
        if (string.IsNullOrEmpty(Config.FolderPath))
        {
            _trigger.Dispatcher.BeginInvoke(() =>
            {
                MessageBox.Show(
                    "欢迎使用 VoidTidy!\n\n请右键托盘图标 → 设置,先指定一个收纳文件夹。",
                    "VoidTidy", MessageBoxButton.OK, MessageBoxImage.Information);
            }, DispatcherPriority.ApplicationIdle);
        }
    }

    private int _tickCount;
    private int _outsideTicks;
    private void OnTick()
    {
        UpdateFullscreenVisibility();
        UpdateAutoHide();
        // 主窗口隐藏状态下,每 30 秒(120 tick × 250ms)trim 一次工作集
        if (++_tickCount % 120 == 0 && _main is { IsVisible: false })
            TrimWorkingSet();
    }

    /// <summary>
    /// 兜底:主窗口可见且配置为「鼠标离开自动隐藏」时,周期性检查鼠标真实坐标。
    /// 连续 N 次都在窗口外才隐藏 → 等价 N×250ms 的延时,顺便忽略瞬时抖动。
    /// 这条路径绕开了 WPF MouseLeave(它会在 ToolTip / 拖拽 / 子控件切换时假触发或不触发)。
    /// </summary>
    private void UpdateAutoHide()
    {
        if (_main is null || !_main.IsVisible) { _outsideTicks = 0; return; }
        if (_main.IsCursorInsideWindow_PublicCheck() || _main.IsSuppressingHide_PublicCheck())
        {
            _outsideTicks = 0;
            return;
        }
        if (Config.Hide != HideMode.OnMouseLeave) { _outsideTicks = 0; return; }

        // 配置的延时 / 250ms = 需要连续多少 tick 才隐藏(至少 2 tick = 500ms,避免太敏感)
        int need = Math.Max(2, Config.HideDelayMs / 250);
        if (++_outsideTicks >= need)
        {
            _outsideTicks = 0;
            _main.HideWithAnimation();
        }
    }

    private static void TrimWorkingSet()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Optimized, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            Native.NativeMethods.EmptyWorkingSet(Native.NativeMethods.GetCurrentProcess());
        }
        catch { /* 失败也无所谓 */ }
    }

    private void UpdateFullscreenVisibility()
    {
        if (_trigger is null) return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(_trigger).Handle;
        var fs = FullscreenDetector.IsForegroundFullscreenOn(hwnd);
        _trigger.SetSuppressed(fs);
        if (fs && _main is { IsVisible: true })
            _main.HideWithAnimation();
    }

    public void ShowMain()
    {
        if (_main is null) return;
        _main.RefreshItems();
        _main.ShowWithAnimation();
    }

    public void HideMain()
    {
        _main?.HideWithAnimation();
    }

    /// <summary>
    /// 从外部(如 TriggerWindow)接到 OLE 文件拖放后转发给主窗口。
    /// 触发条和主窗口在屏幕上挨着,从桌面拖文件时用户可能在任意一边松手,
    /// 让两边都能接住,避免拖了半天没反应。
    /// </summary>
    public void OnFilesDroppedExternal(string[] paths)
    {
        if (_main is null || paths.Length == 0) return;
        if (!_main.IsVisible) ShowMain();
        _main.AcceptFiles(paths);
    }

    public void OnDockChanged()
    {
        _trigger?.ApplyDock();
        _main?.ApplyDock();
        ConfigStore.Save(Config);
    }

    public void OnDockChangedNoSave()
    {
        _trigger?.ApplyDock();
        _main?.ApplyDock();
    }

    public void OnFolderChanged(string folder)
    {
        Config.FolderPath = folder;
        Binder.Bind(folder);
        ConfigStore.Save(Config);
        _main?.RefreshItems();
    }

    public void OnFolderChangedNoSave(string folder)
    {
        Config.FolderPath = folder;
        Binder.Bind(folder);
        _main?.RefreshItems();
    }

    /// <summary>主题(暗/亮模式)变了。重新刷主窗口色板 + DWM 圆角。</summary>
    public void OnThemeChanged()
    {
        if (_main is null) return;
        _main.ApplyTheme();
        // 重新生成 tabs/icons,让动态前景色立刻生效;主窗口可能没显示,也能直接重建
        _main.ApplyDock();  // 内部会 RebuildTabs + RefreshItems
    }

    public void OnIconSizeChanged()
    {
        _main?.RefreshItems();
    }

    public void SaveConfig() => ConfigStore.Save(Config);

    public void Quit()
    {
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _fullscreenTimer?.Stop();
        Binder.Dispose();
        _tray?.Dispose();
    }

    public static void Launch(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true, // 关键:.lnk 必须走 Shell
                WorkingDirectory = System.IO.Path.GetDirectoryName(filePath) ?? "",
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动失败:{ex.Message}", "VoidTidy",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
