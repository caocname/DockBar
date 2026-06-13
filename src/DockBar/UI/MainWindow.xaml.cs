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
        MouseLeave += OnMouseLeave;
        MouseEnter += (_, _) => _hideTimer.Stop();
        // 用 Preview 隧道事件,从 Window 顶层拦截,确保子元素(透明背景区域)不会吃掉拖拽
        PreviewDragEnter += OnDragEnterAny;
        PreviewDragOver  += OnDragEnterAny;
        PreviewDrop      += OnFilesDropped;
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
        // 放行 Shell 的拖放消息,避开 UIPI 过滤(WM_DROPFILES / WM_COPYDATA / WM_COPYGLOBALDATA)。
        // 没有这一步,从某些进程拖文件过来 OLE Drop 可能不触发。
        ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES,      MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA,       MSGFLT_ALLOW, IntPtr.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, IntPtr.Zero);
        // SourceInitialized 时 HWND 已就绪,可以贴亚克力
        ApplyTheme();
    }

    /// <summary>
    /// 主题切换:重新指向当前主题的色板。
    /// 主窗口已不用 AllowsTransparency + 亚克力(为修复内部拖拽漏到桌面的问题),
    /// 只切色板就够了;窗口本身的 Background 也按主题刷新。
    /// </summary>
    public void ApplyTheme()
    {
        var cfg = AppHost.Current!.Config;

        // 文字/边框资源重新指向当前主题
        Resources["DynamicForeground"]    = (Brush)FindResource(cfg.DarkMode ? "Foreground" : "ForegroundLight");
        Resources["DynamicForegroundDim"] = (Brush)FindResource(cfg.DarkMode ? "ForegroundDim" : "ForegroundDimLight");
        Resources["DynamicHover"]         = (Brush)FindResource(cfg.DarkMode ? "SurfaceHover" : "SurfaceHoverLight");

        // 整窗背景:暗色 = 深灰,亮色 = 浅灰。实色,不再走亚克力。
        Background = new SolidColorBrush(cfg.DarkMode
            ? Color.FromRgb(0x1F, 0x1F, 0x1F)
            : Color.FromRgb(0xF5, 0xF5, 0xF5));
    }

    private void ApplyAcrylicMica()
    {
        // 留空兼容老调用;主窗口不再用亚克力
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

    /// <summary>
    /// 顶层拦截 DragEnter/DragOver。区分两种来源:
    ///  ① 外部资源管理器/桌面拖文件进来 → DataFormats.FileDrop,给 Move effect 表示接收;
    ///  ② 内部图标拖拽(DRAG_FORMAT) → 这里不动,留给 Border/Tab 自己的 Drop handler 决定排序/换分类。
    /// 不识别的格式给 None,光标会变禁止符,提示用户。
    /// </summary>
    private void OnDragEnterAny(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        else if (e.Data.GetDataPresent(DRAG_FORMAT))
        {
            // 内部拖拽:默认 Move,具体由更内层的 Drop handler 接管
            e.Effects = DragDropEffects.Move;
            // 不 Handled,让事件继续冒泡到 Border/Tab
        }
    }

    private void OnFilesDropped(object sender, DragEventArgs e)
    {
        // 内部拖拽不在这里处理(每个 Border/Tab 自己有 Drop handler 并 Handled=true)
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        AcceptFiles(paths);
    }

    /// <summary>
    /// 把外部拖入的文件路径列表落到绑定文件夹 + 当前分类。
    /// 同时给 TriggerWindow 上松手的拖放转发用。
    /// 行为:Move(把源头搬过来),复制不会给用户两份。
    /// </summary>
    public void AcceptFiles(string[] paths)
    {
        var cfg = AppHost.Current!.Config;
        if (string.IsNullOrEmpty(cfg.FolderPath) || !Directory.Exists(cfg.FolderPath))
        {
            MessageBox.Show("请先在「设置」中绑定收纳文件夹。", "DockBar");
            return;
        }
        bool any = false;
        foreach (var p in paths.Where(FolderBinder.IsApp))
        {
            try
            {
                var dst = Path.Combine(cfg.FolderPath!, Path.GetFileName(p));
                // 同盘 → File.Move 直接搬;跨盘 → Move 也支持(内部会 Copy+Delete)
                // 已存在同名 → 当作「已经收过」,把源头删掉避免桌面残留
                if (string.Equals(Path.GetFullPath(p), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
                {
                    // 源就是目标(用户从绑定文件夹本身拖回来),啥也不做
                }
                else if (File.Exists(dst))
                {
                    try { File.Delete(p); } catch { /* 删不掉就算了,核心需求是窗口里能看到 */ }
                }
                else
                {
                    File.Move(p, dst);
                }
                // 把它分配到当前分类
                var current = GetOrCreateCurrentCategory();
                if (!current.Files.Contains(Path.GetFileName(dst)))
                    current.Files.Add(Path.GetFileName(dst));
                any = true;
            }
            catch { /* 忽略 */ }
        }
        if (!any) return;
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
        if (e.Data.GetDataPresent(DRAG_FORMAT))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        // 外部 FileDrop 不要在这里 Handled,让它冒泡到顶层 PreviewDrop;
        // 这样从桌面把文件直接拖到分类标签上也能落到「绑定文件夹 + 当前分类」
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

    /// <summary>
    /// 把 fileName 在「当前分类」内挪到 targetFileName 的前/后。
    /// 同分类排序用,不跨分类。
    /// </summary>
    private void ReorderInCurrent(string fileName, string targetFileName, bool insertAfter)
    {
        if (string.Equals(fileName, targetFileName, StringComparison.OrdinalIgnoreCase)) return;
        var cur = GetOrCreateCurrentCategory();
        // 用大小写不敏感比较找索引
        int srcIdx = cur.Files.FindIndex(f => f.Equals(fileName,       StringComparison.OrdinalIgnoreCase));
        if (srcIdx < 0)
        {
            // 跨分类拖到图标上 = 先挪过来再排序
            foreach (var c in AppHost.Current!.Config.Categories)
                c.Files.RemoveAll(f => f.Equals(fileName, StringComparison.OrdinalIgnoreCase));
            cur.Files.Add(fileName);
            srcIdx = cur.Files.Count - 1;
        }
        var item = cur.Files[srcIdx];
        cur.Files.RemoveAt(srcIdx);
        int tgtIdx = cur.Files.FindIndex(f => f.Equals(targetFileName, StringComparison.OrdinalIgnoreCase));
        if (tgtIdx < 0) tgtIdx = cur.Files.Count;
        if (insertAfter) tgtIdx++;
        if (tgtIdx > cur.Files.Count) tgtIdx = cur.Files.Count;
        cur.Files.Insert(tgtIdx, item);
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
            // 让本图标也能做 drop target,实现「拖到图标上 = 在它前/后插入」
            AllowDrop = true,
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
        // 按住左键拖动 → 只塞 DRAG_FORMAT 这一种自定义格式。
        // 关键:不要再塞 DataFormats.FileDrop。否则会有两个连锁灾难:
        //  ① 光标稍微滑出窗口边缘,桌面/资源管理器看到 FileDrop 直接把文件 Move 走 → 图标飞到桌面;
        //  ② 即使在窗口内松手,顶层 PreviewDrop 走 tunneling 比 Border.Drop 先到,
        //     OnFilesDropped 看到 FileDrop 就 Handled=true 把事件吃掉,内部排序根本轮不到执行。
        // 内部拖拽只在窗口里有意义(换分类 / 排序),不需要让外部目标识别。
        // 后续如果要实现「拖到桌面 = 把文件搬出去」,应加一个明确的辅助键(如按住 Alt)再附加 FileDrop,
        // 默认形态保持现在这样最安全。
        System.Windows.Point startPt = default;
        border.PreviewMouseLeftButtonDown += (_, e) => startPt = e.GetPosition(this);
        border.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            // 拖出 6px 才认作拖拽,否则当点击
            if (Math.Abs(p.X - startPt.X) < 6 && Math.Abs(p.Y - startPt.Y) < 6) return;
            var data = new System.Windows.DataObject(DRAG_FORMAT, item.FileName);
            // 拖拽期间禁止隐藏。结束后让 250ms tick 用真坐标决定要不要缩
            _suppressMouseLeave = true;
            _hideTimer.Stop();
            try
            {
                DragDrop.DoDragDrop(border, data, DragDropEffects.Move);
            }
            finally
            {
                _suppressMouseLeave = false;
            }
        };
        // 同分类内排序:把别的图标拖到我身上 = 在我前/后插入
        border.DragEnter += (_, e) => OnIconDragOver(e);
        border.DragOver  += (_, e) => OnIconDragOver(e);
        border.Drop      += (_, e) => OnIconDrop(e, item, border);
        return border;
    }

    /// <summary>同分类内排序:只接受内部图标拖拽,外部 FileDrop 留给顶层 PreviewDrop 处理。</summary>
    private static void OnIconDragOver(System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DRAG_FORMAT))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        // 不是内部格式就不 Handled,让 FileDrop 冒泡到顶层 PreviewDrop
    }

    private void OnIconDrop(System.Windows.DragEventArgs e, AppItem target, FrameworkElement targetElem)
    {
        if (!e.Data.GetDataPresent(DRAG_FORMAT)) return;
        var fn = (string)e.Data.GetData(DRAG_FORMAT);
        // 鼠标落点在 target 中点之前 → 插到 target 之前;之后 → 插到 target 之后
        var pos = e.GetPosition(targetElem);
        bool after = pos.X > targetElem.RenderSize.Width / 2;
        ReorderInCurrent(fn, target.FileName, after);
        e.Handled = true;
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
