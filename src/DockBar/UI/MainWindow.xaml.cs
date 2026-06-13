using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DockBar.Native;
using DockBar.Services;
using static DockBar.Native.NativeMethods;

namespace DockBar.UI;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _hideTimer;
    private bool _suppressMouseLeave;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => ApplyAcrylicMica();
        MouseLeave += OnMouseLeave;
        MouseEnter += (_, _) => _hideTimer.Stop();
        Drop += OnFilesDropped;
        DragOver += (_, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
        IsVisibleChanged += (_, e) =>
        {
            // 隐藏后释放工作集回 OS,任务管理器看到的内存大幅下降
            if (e.NewValue is false)
                EmptyWorkingSet(GetCurrentProcess());
        };

        _hideTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(AppHost.Current?.Config.HideDelayMs ?? 600)
        };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            // 用 Win32 真坐标做最后一道闸:鼠标如果还压在窗口内,说明 MouseLeave 是 ToolTip / 拖拽 / 子控件抖动假信号
            if (IsCursorInsideWindow()) return;
            HideWithAnimation();
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        // SourceInitialized 时 HWND 已就绪,可以贴亚克力
        ApplyTheme();
    }

    /// <summary>
    /// 把当前 DarkMode/Acrylic 配置应用到本窗口。
    /// 改主题后调用一次就行,不需要重建子控件。
    /// </summary>
    public void ApplyTheme()
    {
        var cfg = AppHost.Current!.Config;
        if (cfg.UseAcrylic)
            WindowEffects.ApplyAcrylic(this, dark: cfg.DarkMode);
        else
            WindowEffects.ApplyBlur(this, dark: cfg.DarkMode, tintAlpha: 0xAA);

        // 文字/边框资源重新指向当前主题
        Resources["DynamicForeground"]    = (Brush)FindResource(cfg.DarkMode ? "Foreground" : "ForegroundLight");
        Resources["DynamicForegroundDim"] = (Brush)FindResource(cfg.DarkMode ? "ForegroundDim" : "ForegroundDimLight");
        Resources["DynamicHover"]         = (Brush)FindResource(cfg.DarkMode ? "SurfaceHover" : "SurfaceHoverLight");
    }

    private void ApplyAcrylicMica()
    {
        // 留空兼容老调用;真正的亚克力在 OnSourceInitialized 里贴
    }

    public void ApplyDock()
    {
        var cfg = AppHost.Current!.Config;
        var work = SystemParameters.WorkArea;

        // 顶部停靠 → 横向铺,标签栏在上,网格在下
        // 侧边停靠 → 竖向铺,标签栏在上(短边方向),网格滚动
        switch (cfg.Dock)
        {
            case DockSide.Top:
                Width  = cfg.WindowLengthPx;
                Height = cfg.WindowDepthPx;
                Left = work.Left + (work.Width - Width) * cfg.TriggerOffsetRatio;
                Top  = work.Top;
                DockPanel.SetDock(Tabs, Dock.Top);
                break;
            case DockSide.Left:
                Width  = cfg.WindowDepthPx;
                Height = cfg.WindowLengthPx;
                Left = work.Left;
                Top  = work.Top + (work.Height - Height) * cfg.TriggerOffsetRatio;
                DockPanel.SetDock(Tabs, Dock.Top);
                break;
            case DockSide.Right:
                Width  = cfg.WindowDepthPx;
                Height = cfg.WindowLengthPx;
                Left = work.Right - Width;
                Top  = work.Top + (work.Height - Height) * cfg.TriggerOffsetRatio;
                DockPanel.SetDock(Tabs, Dock.Top);
                break;
        }
        RebuildTabs();
        RefreshItems();
    }

    public void ShowWithAnimation()
    {
        if (IsVisible) { _hideTimer.Stop(); return; }
        ApplyDock();
        Visibility = Visibility.Visible;
        var cfg = AppHost.Current!.Config;
        var from = cfg.Dock switch
        {
            DockSide.Top => new Thickness(0, -8, 0, 0),
            DockSide.Left => new Thickness(-8, 0, 0, 0),
            _ => new Thickness(8, 0, 0, 0)
        };
        Root.Margin = from;
        var anim = new ThicknessAnimation
        {
            To = new Thickness(0),
            Duration = TimeSpan.FromMilliseconds(140),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Root.BeginAnimation(Border.MarginProperty, anim);
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
    }

    public void HideWithAnimation()
    {
        if (!IsVisible) return;
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
        anim.Completed += (_, _) => Visibility = Visibility.Collapsed;
        BeginAnimation(OpacityProperty, anim);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_suppressMouseLeave) return;
        var cfg = AppHost.Current!.Config;
        if (cfg.Hide == HideMode.OnMouseLeave)
        {
            _hideTimer.Interval = TimeSpan.FromMilliseconds(cfg.HideDelayMs);
            _hideTimer.Start();
        }
    }

    /// <summary>用 Win32 真实光标坐标判定鼠标是否还在本窗口矩形内,避开 WPF MouseLeave 假信号。</summary>
    private bool IsCursorInsideWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return false;
        if (!GetCursorPos(out var pt)) return false;
        if (!GetWindowRect(hwnd, out var r)) return false;
        return pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom;
    }

    /// <summary>给 AppHost 周期检查用。</summary>
    public bool IsCursorInsideWindow_PublicCheck() => IsCursorInsideWindow();
    /// <summary>给 AppHost 周期检查用。</summary>
    public bool IsSuppressingHide_PublicCheck() => _suppressMouseLeave;

    // ----- 拖入文件:复制到绑定文件夹,只接受 lnk/exe -----
    private void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var cfg = AppHost.Current!.Config;
        if (string.IsNullOrEmpty(cfg.FolderPath) || !Directory.Exists(cfg.FolderPath))
        {
            MessageBox.Show("请先在「设置」中绑定收纳文件夹。", "DockBar");
            return;
        }
        foreach (var p in paths.Where(FolderBinder.IsApp))
        {
            try
            {
                var dst = Path.Combine(cfg.FolderPath!, Path.GetFileName(p));
                if (!File.Exists(dst)) File.Copy(p, dst);
                // 把它分配到当前分类
                var current = GetOrCreateCurrentCategory();
                if (!current.Files.Contains(Path.GetFileName(dst)))
                    current.Files.Add(Path.GetFileName(dst));
            }
            catch { /* 忽略 */ }
        }
        AppHost.Current!.SaveConfig();
        RefreshItems();
    }

    // ----- 分类标签 -----
    private CategoryConfig GetOrCreateCurrentCategory()
    {
        var cfg = AppHost.Current!.Config;
        if (cfg.Categories.Count == 0)
        {
            cfg.Categories.Add(new CategoryConfig { Name = "默认" });
            cfg.CurrentCategoryId = cfg.Categories[0].Id;
        }
        return cfg.Categories.FirstOrDefault(c => c.Id == cfg.CurrentCategoryId)
            ?? cfg.Categories[0];
    }

    private void RebuildTabs()
    {
        var cfg = AppHost.Current!.Config;
        if (cfg.Categories.Count == 0)
        {
            cfg.Categories.Add(new CategoryConfig { Name = "默认" });
            cfg.CurrentCategoryId = cfg.Categories[0].Id;
        }
        cfg.CurrentCategoryId ??= cfg.Categories[0].Id;

        Tabs.Items.Clear();
        foreach (var cat in cfg.Categories)
        {
            var btn = new ToggleButton
            {
                Content = cat.Name,
                Style = (Style)FindResource("TabButtonStyle"),
                IsChecked = cat.Id == cfg.CurrentCategoryId,
                Tag = cat.Id,
                Margin = new Thickness(0, 0, 4, 0),
                Foreground = cat.Id == cfg.CurrentCategoryId ? Fg() : FgDim(),
            };
            btn.Click += (_, _) =>
            {
                cfg.CurrentCategoryId = (string)btn.Tag;
                AppHost.Current!.SaveConfig();
                RebuildTabs();
                RefreshItems();
            };
            // 右键管理(改名/删除)
            btn.MouseRightButtonUp += (_, _) => ShowTabMenu(cat);
            // 接受图标拖拽 → 改归属
            btn.AllowDrop = true;
            btn.DragEnter += (_, e) => OnTabDragOver(e);
            btn.DragOver  += (_, e) => OnTabDragOver(e);
            btn.Drop      += (_, e) => OnTabDrop(e, cat);
            Tabs.Items.Add(btn);
        }
        // 末尾「+」新增分类
        var add = new Button
        {
            Content = "+",
            Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(10, 5, 10, 5),
            FontSize = 14,
            ToolTip = "新增分类",
            Style = (Style)FindResource("GhostButtonStyle"),
            Foreground = FgDim(),
        };
        add.Click += (_, _) =>
        {
            _suppressMouseLeave = true;
            try
            {
                var name = InputBoxWindow.Ask("新增分类", "分类名:", "新分类");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var nc = new CategoryConfig { Name = name.Trim() };
                    cfg.Categories.Add(nc);
                    cfg.CurrentCategoryId = nc.Id;
                    AppHost.Current!.SaveConfig();
                    RebuildTabs();
                    RefreshItems();
                }
            }
            finally { _suppressMouseLeave = false; }
        };
        Tabs.Items.Add(add);

        // 「管理当前分类」按钮 — 打开图标多选对话框,批量加入/移除
        var mgr = new Button
        {
            Content = "管理…",
            Padding = new Thickness(10, 5, 10, 5),
            ToolTip = "批量勾选当前分类要包含哪些图标",
            Style = (Style)FindResource("GhostButtonStyle"),
            Foreground = FgDim(),
        };
        mgr.Click += (_, _) =>
        {
            var cur = GetOrCreateCurrentCategory();
            _suppressMouseLeave = true;
            try
            {
                var dlg = new IconPickerWindow(AppHost.Current!, cur) { Owner = this };
                if (dlg.ShowDialog() == true) RefreshItems();
            }
            finally { _suppressMouseLeave = false; }
        };
        Tabs.Items.Add(mgr);
    }

    private void ShowTabMenu(CategoryConfig cat)
    {
        var cfg = AppHost.Current!.Config;
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "重命名" };
        rename.Click += (_, _) =>
        {
            var name = InputBoxWindow.Ask("重命名分类", "新名字:", cat.Name);
            if (!string.IsNullOrWhiteSpace(name)) { cat.Name = name.Trim(); AppHost.Current!.SaveConfig(); RebuildTabs(); }
        };
        menu.Items.Add(rename);
        var del = new MenuItem { Header = "删除" };
        del.Click += (_, _) =>
        {
            if (cfg.Categories.Count <= 1) { MessageBox.Show("至少保留一个分类。"); return; }
            cfg.Categories.Remove(cat);
            cfg.CurrentCategoryId = cfg.Categories[0].Id;
            AppHost.Current!.SaveConfig();
            RebuildTabs();
            RefreshItems();
        };
        menu.Items.Add(del);
        menu.IsOpen = true;
    }

    // ----- 拖拽改归属(图标 → 标签) -----
    private const string DRAG_FORMAT = "DockBar.IconFileName";

    private static void OnTabDragOver(System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DRAG_FORMAT)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTabDrop(System.Windows.DragEventArgs e, CategoryConfig target)
    {
        if (!e.Data.GetDataPresent(DRAG_FORMAT)) return;
        var fn = (string)e.Data.GetData(DRAG_FORMAT);
        MoveToCategory(fn, target);
        e.Handled = true;
    }

    private void MoveToCategory(string fileName, CategoryConfig target)
    {
        var cfg = AppHost.Current!.Config;
        // 一个文件只属一个分类(需求 2.4.3)
        foreach (var c in cfg.Categories)
            c.Files.RemoveAll(f => f.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (!target.Files.Contains(fileName)) target.Files.Add(fileName);
        AppHost.Current!.SaveConfig();
        RefreshItems();
    }

    // ----- 图标网格 -----
    public void RefreshItems()
    {
        if (AppHost.Current is null) return;
        var cfg = AppHost.Current.Config;
        var binder = AppHost.Current.Binder;
        var cur = GetOrCreateCurrentCategory();

        var all = binder.Scan();
        // 已分配到任意分类的文件名集合
        var assigned = new HashSet<string>(cfg.Categories.SelectMany(c => c.Files), StringComparer.OrdinalIgnoreCase);
        // 当前分类:文件名顺序按 cur.Files;新文件(还没分配过的)默认进默认分类
        var defaultCat = cfg.Categories.First();
        foreach (var item in all)
        {
            if (!assigned.Contains(item.FileName))
            {
                defaultCat.Files.Add(item.FileName);
                assigned.Add(item.FileName);
            }
        }
        // 清理已删除文件
        foreach (var c in cfg.Categories)
            c.Files.RemoveAll(fn => !all.Any(a => a.FileName.Equals(fn, StringComparison.OrdinalIgnoreCase)));

        Items.Items.Clear();
        var byName = all.ToDictionary(a => a.FileName, StringComparer.OrdinalIgnoreCase);
        foreach (var fn in cur.Files)
        {
            if (!byName.TryGetValue(fn, out var item)) continue;
            Items.Items.Add(BuildIcon(item));
        }
    }

    /// <summary>根据当前 DarkMode 选 brush,动态生成元素都用这个,主题切换后重建一次 UI 就生效。</summary>
    private Brush Fg() =>
        (Brush)FindResource(AppHost.Current!.Config.DarkMode ? "Foreground" : "ForegroundLight");
    private Brush FgDim() =>
        (Brush)FindResource(AppHost.Current!.Config.DarkMode ? "ForegroundDim" : "ForegroundDimLight");
    private Brush Hover() =>
        (Brush)FindResource(AppHost.Current!.Config.DarkMode ? "SurfaceHover" : "SurfaceHoverLight");

    private FrameworkElement BuildIcon(AppItem item)
    {
        var cfg = AppHost.Current!.Config;
        var icon = new Image
        {
            Width = cfg.IconSizePx,
            Height = cfg.IconSizePx,
            Source = IconExtractor.Extract(item.FullPath, jumbo: cfg.IconSizePx >= 64),
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

        var label = new TextBlock
        {
            Text = item.DisplayName,
            Foreground = Fg(),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = cfg.IconSizePx + 28,
        };
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(icon);
        stack.Children.Add(label);

        var border = new Border
        {
            Padding = new Thickness(10, 10, 10, 8),
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = stack,
            Tag = item,
            ToolTip = item.DisplayName,
        };
        border.MouseEnter += (_, _) => border.Background = Hover();
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        // 双击挂到 Down,Up 上 ClickCount 在 WPF 里不可靠
        border.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount >= 2)
            {
                e.Handled = true;
                AppHost.Current!.Launch_(item.FullPath);
            }
        };
        border.MouseRightButtonUp += (_, e) =>
        {
            // 右键 = Windows 资源管理器原生菜单(需求 2.5.2)
            // TrackPopupMenuEx 是阻塞的模态消息泵,期间不会触发 WPF MouseLeave/HideTimer
            _suppressMouseLeave = true;
            _hideTimer.Stop();
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                GetCursorPos(out var pt);
                ShellContextMenu.Show(item.FullPath, hwnd, pt.X, pt.Y);
            }
            finally { _suppressMouseLeave = false; }
            e.Handled = true;
        };
        // 按住左键拖动 → 自定义格式,只有标签会接(避免被资源管理器接走)
        System.Windows.Point startPt = default;
        border.PreviewMouseLeftButtonDown += (_, e) => startPt = e.GetPosition(this);
        border.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            // 拖出 6px 才认作拖拽,否则当点击
            if (Math.Abs(p.X - startPt.X) < 6 && Math.Abs(p.Y - startPt.Y) < 6) return;
            var data = new System.Windows.DataObject(DRAG_FORMAT, item.FileName);
            DragDrop.DoDragDrop(border, data, DragDropEffects.Move);
        };
        return border;
    }
}

internal static class AppContextLaunchExt
{
    public static void Launch_(this AppHost ctx, string path)
    {
        AppHost.Launch(path);
        if (ctx.Config.Hide == HideMode.OnLaunch)
            ctx.HideMain();
    }
}
