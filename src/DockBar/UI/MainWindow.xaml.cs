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
        // 任务管理器"应用"分组 / Alt+Tab / 任务栏 都按窗口 Title 显示。
        // 多实例时 App.DisplayName = "VoidTidy{N}",这里同步上去。
        Title = App.DisplayName;
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
        // SourceInitialized 时 HWND 已就绪,可以套 DWM 圆角
        ApplyTheme();
    }

    /// <summary>
    /// 主题切换:重新指向当前主题的色板,并把窗口实色背景刷成对应色调。
    /// 主窗口用实色背景 + DWM 系统级圆角 + 暗色标题区,不依赖 AllowsTransparency / 亚克力。
    /// </summary>
    public void ApplyTheme()
    {
        var cfg = AppHost.Current!.Config;

        // 文字/边框资源重新指向当前主题
        Resources["DynamicForeground"]    = (Brush)FindResource(cfg.DarkMode ? "Foreground" : "ForegroundLight");
        Resources["DynamicForegroundDim"] = (Brush)FindResource(cfg.DarkMode ? "ForegroundDim" : "ForegroundDimLight");
        Resources["DynamicHover"]         = (Brush)FindResource(cfg.DarkMode ? "SurfaceHover" : "SurfaceHoverLight");

        // 整窗背景:暗色 = 深灰,亮色 = 浅灰
        Background = new SolidColorBrush(cfg.DarkMode
            ? Color.FromRgb(0x1F, 0x1F, 0x1F)
            : Color.FromRgb(0xF5, 0xF5, 0xF5));

        // Win11 系统级圆角 + 暗色标题(无标题栏也影响阴影/边线色)。Win10 静默失败。
        WindowEffects.ApplyRoundCorners(this, dark: cfg.DarkMode,
            preference: NativeMethods.DwmWindowCornerPreference.Round);
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
    /// 顶层拦截 DragEnter/DragOver。这条路径只用于"外部拖入":
    /// 桌面 / 资源管理器把文件拖到窗口区域,DataObject 含 FileDrop → 落到绑定文件夹。
    /// 窗口内的图标拖拽走的是 CaptureMouse + MouseMove 自管路径,根本不会触发 OLE,
    /// 所以这里见到 FileDrop 一律当作外部来源处理,不需要再判别"是不是自己人"。
    /// </summary>
    private void OnDragEnterAny(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }

    private void OnFilesDropped(object sender, DragEventArgs e)
    {
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
            MessageBox.Show("请先在「设置」中绑定收纳文件夹。", "VoidTidy");
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

    // ===================================================================
    // 窗口内图标拖拽 — 完全自管,不走 OLE。
    // ===================================================================
    // 历史:之前用 DragDrop.DoDragDrop 让 Shell 协商,Win11 24H2 上桌面无论怎么塞 DataObject
    //      都直接 DROPEFFECT_NONE(光标禁止符),拖出窗口到桌面永远不成功。
    // 新方案:从图标 Border 上鼠标按下→移动→松手,我们自己用 CaptureMouse 跟踪。
    //       松手那一刻读 Win32 真实光标坐标,自己判 4 种落点:
    //         ① 同分类内另一个图标 → 在它前/后插入(排序)
    //         ② 分类标签 → 换分类
    //         ③ 窗口矩形外 + 命中 Shell 桌面 → File.Move 到桌面目录
    //         ④ 其它(空白) → 不动
    //      不调 DoDragDrop = 不依赖 OLE = 桌面拒不拒绝都跟我们无关。
    //
    // 外部拖入(桌面 → 本窗口)仍走 WPF OLE 原路径(顶层 PreviewDrop + AcceptFiles),
    // 那条路 Shell 是源,我们是目标,DataObject 是 Explorer 自己造的合法 CF_HDROP,从来都正常。
    private const string DRAG_FORMAT = "DockBar.IconFileName"; // 弃用,留作历史标记

    /// <summary>正在被拖动的图标 FileName,null = 当前没在拖。</summary>
    private string? _dragFileName;
    /// <summary>正在被拖的图标 Border,松手时清掉它的视觉态;也用来 ReleaseMouseCapture。</summary>
    private Border? _dragBorder;
    /// <summary>当前显示拖拽指示线的目标 Border(null = 未指向任何图标)。每帧最多一个。</summary>
    private Border? _dragHighlightTarget;

    private static void OnTabDragOver(System.Windows.DragEventArgs e)
    {
        // 窗口内图标拖拽不走 OLE,所以这里只可能是外部 FileDrop。
        // 我们不在 Tab 上 Handled=true,让 FileDrop 冒泡到顶层 PreviewDrop,
        // 走 AcceptFiles → GetOrCreateCurrentCategory(); "落到当前分类"对用户更直觉,
        // 比"落到拖到的那个 Tab 但不能切到那"更顺手。
    }

    private void OnTabDrop(System.Windows.DragEventArgs e, CategoryConfig target)
    {
        // 同上,留给顶层处理,这里是空 handler 以兼容签名。
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
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Child = stack,
            Tag = item,
            ToolTip = item.DisplayName,
            // 让本图标也能做 drop target,实现「拖到图标上 = 在它前/后插入」
            AllowDrop = true,
        };
        // hover:轻底色 + 极淡描边,放在拖拽 effect 之外的视觉反馈
        border.MouseEnter += (_, _) =>
        {
            border.Background = Hover();
            border.BorderBrush = (Brush)FindResource("Stroke");
        };
        border.MouseLeave += (_, _) =>
        {
            border.Background = Brushes.Transparent;
            border.BorderBrush = Brushes.Transparent;
        };
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
        // ----- 自管拖拽:按下 → 移动 → 松手 -----
        // 见类顶部 _dragFileName 注释,所有逻辑见 BeginIconDrag / OnIconDragMove / EndIconDrag。
        System.Windows.Point startPt = default;
        border.PreviewMouseLeftButtonDown += (_, e) =>
        {
            // 双击落点也会进这里(ClickCount==2),但上面的 MouseLeftButtonDown 已经先 Handled
            // 把双击吃掉了,这里只会拿到 ClickCount==1 的单击。记起点供阈值判断。
            if (e.ClickCount >= 2) return;
            startPt = e.GetPosition(this);
        };
        border.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            // 已经在拖了 → 走移动分支
            if (_dragFileName != null) { OnIconDragMove(); return; }
            // 还没拖 → 检查是否过了启动阈值
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - startPt.X) < 6 && Math.Abs(p.Y - startPt.Y) < 6) return;
            BeginIconDrag(border, item);
        };
        border.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_dragFileName != null) EndIconDrag(commit: true);
        };
        border.LostMouseCapture += (_, _) =>
        {
            // 用户按 Esc / 切到别的窗口 / 我们自己 Release → 当作取消
            if (_dragFileName != null) EndIconDrag(commit: false);
        };
        return border;
    }

    // ---------- 自管拖拽实现 ----------

    private void BeginIconDrag(Border source, AppItem item)
    {
        _dragFileName = item.FileName;
        _dragBorder = source;
        _suppressMouseLeave = true;
        _hideTimer.Stop();
        // 给个抓手光标作反馈;Border 上 hover 时设的 Cursors.Hand 在 capture 期间被覆盖
        Mouse.OverrideCursor = Cursors.Hand;
        // 捕获到 Window 级别,光标移出 Border 也能继续收 MouseMove
        // (Border.CaptureMouse 也行,但移出 Window 后就没事件了;我们要监听到松手在桌面上的情况,
        //  所以捕到主窗口 HWND 上更稳;不过 WPF 的 mouse capture 跨 HWND 仍受限,
        //  我们用主循环 PreviewMouseMove + Win32 真实坐标兜底)
        source.CaptureMouse();
    }

    /// <summary>拖拽过程中:实时高亮"鼠标当前指向的图标"作为目标提示。</summary>
    private void OnIconDragMove()
    {
        // 用 Win32 拿真实屏幕坐标,在自己的窗口里做命中测试(转回 Window 客户区坐标)
        if (!GetCursorPos(out var screenPt)) return;
        var winPt = PointFromScreen(new System.Windows.Point(screenPt.X, screenPt.Y));

        Border? newTarget = null;
        bool? newAfter = null;
        // VisualTreeHelper.HitTest 在窗口客户区做命中,沿 visual 树找最近的 AppItem-tagged Border
        var hit = VisualTreeHelper.HitTest(this, winPt);
        if (hit?.VisualHit is DependencyObject d)
        {
            for (var cur = d; cur != null; cur = VisualTreeHelper.GetParent(cur))
            {
                if (cur is Border b && b.Tag is AppItem ai && b != _dragBorder)
                {
                    var pos = Mouse.GetPosition(b);
                    newTarget = b;
                    newAfter = pos.X > b.RenderSize.Width / 2;
                    break;
                }
            }
        }

        // 切高亮:旧的清掉,新的画一条
        if (_dragHighlightTarget != null && _dragHighlightTarget != newTarget)
            ClearDropIndicator(_dragHighlightTarget);
        _dragHighlightTarget = newTarget;
        if (newTarget != null && newAfter.HasValue)
        {
            newTarget.BorderBrush = (Brush)FindResource("Accent");
            newTarget.BorderThickness = newAfter.Value
                ? new Thickness(0, 0, 2, 0)
                : new Thickness(2, 0, 0, 0);
        }
    }

    /// <summary>松手:根据光标当前位置决定四种动作之一。</summary>
    private void EndIconDrag(bool commit)
    {
        var fn = _dragFileName;
        var src = _dragBorder;
        _dragFileName = null;
        _dragBorder = null;
        Mouse.OverrideCursor = null;

        if (_dragHighlightTarget != null)
        {
            ClearDropIndicator(_dragHighlightTarget);
            _dragHighlightTarget = null;
        }
        if (src != null && src.IsMouseCaptured) src.ReleaseMouseCapture();

        // 标志位推迟一帧关掉,免得 ReleaseMouseCapture 之后 250ms tick 误判把窗口收起来
        Dispatcher.BeginInvoke(new Action(() => _suppressMouseLeave = false),
            System.Windows.Threading.DispatcherPriority.Background);

        if (!commit || fn == null) return;
        if (src == null || src.Tag is not AppItem srcItem) return;

        if (!GetCursorPos(out var screenPt)) return;

        // 先看是不是落在窗口里。落在窗口里 → 排序/换分类;落在外面 → 桌面 fallback。
        if (TryGetWindowClientPoint(screenPt, out var winPt))
        {
            // 1) 命中分类标签 → 换分类
            var hit = VisualTreeHelper.HitTest(this, winPt);
            if (hit?.VisualHit is DependencyObject d1)
            {
                for (var cur = d1; cur != null; cur = VisualTreeHelper.GetParent(cur))
                {
                    if (cur is System.Windows.Controls.Primitives.ToggleButton tb &&
                        tb.Tag is string catId)
                    {
                        var cat = AppHost.Current!.Config.Categories
                            .FirstOrDefault(c => c.Id == catId);
                        if (cat != null) MoveToCategory(fn, cat);
                        return;
                    }
                }
                // 2) 命中另一个图标 → 排序
                for (var cur = d1; cur != null; cur = VisualTreeHelper.GetParent(cur))
                {
                    if (cur is Border b && b.Tag is AppItem ai && b != src)
                    {
                        var local = Mouse.GetPosition(b);
                        bool after = local.X > b.RenderSize.Width / 2;
                        ReorderInCurrent(fn, ai.FileName, after);
                        return;
                    }
                }
            }
            // 3) 窗口内但落空 → 不动
            return;
        }

        // 4) 落在窗口外 → 命中桌面就 File.Move 过去
        TryMoveSourceToDesktopIfCursorOnDesktop(srcItem.FullPath);
    }

    /// <summary>把屏幕坐标换成窗口客户区坐标;返回 false 表示坐标在窗口矩形外。</summary>
    private bool TryGetWindowClientPoint(NativeMethods.POINT screenPt, out System.Windows.Point winPt)
    {
        winPt = default;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return false;
        if (!GetWindowRect(hwnd, out var r)) return false;
        if (screenPt.X < r.Left || screenPt.X >= r.Right ||
            screenPt.Y < r.Top  || screenPt.Y >= r.Bottom) return false;
        winPt = PointFromScreen(new System.Windows.Point(screenPt.X, screenPt.Y));
        return true;
    }

    /// <summary>松手时光标命中桌面就把绑定文件夹里的源文件 Move 过去,呈现"图标飞到桌面"的视觉效果。</summary>
    private void TryMoveSourceToDesktopIfCursorOnDesktop(string srcPath)
    {
        try
        {
            if (!File.Exists(srcPath)) return;
            if (!GetCursorPos(out var pt)) return;
            var hwnd = WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return;

            // 自己的窗口(主窗口/触发条)就不算"拖出去"
            var myMain = new WindowInteropHelper(this).Handle;
            var root = GetAncestor(hwnd, GA_ROOT);
            if (root == myMain) return;

            // 桌面探测:沿 hwnd 链一路上行,任意一层类名命中 Progman/WorkerW/SHELLDLL_DefView/SysListView32
            // 都算桌面,也兼容 GetShellWindow() 直接命中。Win11 桌面 host 在多 monitor 下层级会变。
            bool isDesktop = false;
            var shellWnd = GetShellWindow();
            for (var cur = hwnd; cur != IntPtr.Zero; cur = GetAncestor(cur, GA_PARENT))
            {
                if (cur == shellWnd) { isDesktop = true; break; }
                var sb = new System.Text.StringBuilder(64);
                GetClassName(cur, sb, sb.Capacity);
                var cls = sb.ToString();
                if (cls is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "SysListView32")
                {
                    isDesktop = true;
                    break;
                }
                if (cur == root) break;
            }
            if (!isDesktop) return;

            var destDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!Directory.Exists(destDir)) return;

            var dst = Path.Combine(destDir, Path.GetFileName(srcPath));
            // 同名加 (2)
            int n = 2;
            while (File.Exists(dst))
            {
                var name = Path.GetFileNameWithoutExtension(srcPath);
                var ext = Path.GetExtension(srcPath);
                dst = Path.Combine(destDir, $"{name} ({n}){ext}");
                n++;
                if (n > 99) return;
            }
            File.Move(srcPath, dst);
            RefreshItems();
        }
        catch { /* 失败不打扰用户,文件还在窗口里 */ }
    }

    /// <summary>把图标 Border 的拖拽落点提示清掉,恢复成 hover/默认态。</summary>
    private void ClearDropIndicator(Border target)
    {
        target.BorderThickness = new Thickness(1);
        target.BorderBrush = target.IsMouseOver
            ? (Brush)FindResource("Stroke")
            : Brushes.Transparent;
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
