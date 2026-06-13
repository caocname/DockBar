using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace DockBar.Services;

public sealed record AppItem(string FullPath, string FileName, string DisplayName);

/// <summary>
/// 监听绑定文件夹,只识别 .lnk / .exe(需求 2.3.3 强制限制)。
/// 用 FileSystemWatcher,变化合并去抖到 200ms,避免文件保存时连续触发刷新。
/// </summary>
internal sealed class FolderBinder : IDisposable
{
    private FileSystemWatcher? _watcher;
    private string? _folder;
    private readonly DispatcherTimer _debounce;
    private readonly Dispatcher _ui;

    public event Action? Changed;

    public string? Folder => _folder;

    public FolderBinder(Dispatcher uiDispatcher)
    {
        _ui = uiDispatcher;
        _debounce = new DispatcherTimer(DispatcherPriority.Background, _ui)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Changed?.Invoke();
        };
    }

    public void Bind(string? folder)
    {
        _folder = folder;
        _watcher?.Dispose();
        _watcher = null;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        // FileSystemWatcher 单 Filter 不能多扩展名,直接全收,在事件里筛
        _watcher.Created += (_, _) => Schedule();
        _watcher.Deleted += (_, _) => Schedule();
        _watcher.Renamed += (_, _) => Schedule();
        _watcher.Changed += (_, _) => Schedule();
    }

    private void Schedule()
    {
        // 切回 UI 线程触发去抖
        _ui.BeginInvoke(() => { _debounce.Stop(); _debounce.Start(); });
    }

    /// <summary>同步扫描当前绑定文件夹下所有 lnk/exe。</summary>
    public IReadOnlyList<AppItem> Scan()
    {
        if (string.IsNullOrEmpty(_folder) || !Directory.Exists(_folder))
            return Array.Empty<AppItem>();
        try
        {
            return Directory.EnumerateFiles(_folder)
                .Where(IsApp)
                .Select(p => new AppItem(p,
                    Path.GetFileName(p),
                    Path.GetFileNameWithoutExtension(p)))
                .OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<AppItem>();
        }
    }

    public static bool IsApp(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".exe", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Stop();
    }
}
