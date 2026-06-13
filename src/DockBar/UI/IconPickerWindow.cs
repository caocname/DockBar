using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DockBar.Native;
using DockBar.Services;

namespace DockBar.UI;

/// <summary>
/// 「管理本分类的图标」对话框:
///  - 列出整个绑定文件夹里所有 lnk/exe
///  - 已属于本分类的复选框默认勾上
///  - 「保存」批量改归属;一个文件只属一个分类(需求 2.4.3)
///  - 「全选」/「全不选」/「移除选中」一键操作
/// </summary>
internal sealed class IconPickerWindow : Window
{
    private readonly AppHost _host;
    private readonly CategoryConfig _target;
    private readonly List<(AppItem item, CheckBox cb, CategoryConfig? owner)> _rows = new();

    public IconPickerWindow(AppHost host, CategoryConfig target)
    {
        _host = host;
        _target = target;
        Title = $"管理「{target.Name}」分类";
        Width = 520;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Foreground = (Brush)Application.Current.FindResource("Foreground");
        SourceInitialized += (_, _) => WindowEffects.ApplyAcrylic(this, dark: host.Config.DarkMode);

        var outer = new Border
        {
            Background = (Brush)Application.Current.FindResource("AcrylicTint"),
            CornerRadius = new CornerRadius(12),
        };
        var root = new DockPanel { Margin = new Thickness(16) };
        outer.Child = root;

        // 标题栏(自绘,因为 WindowStyle=None)
        var titleBar = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(titleBar, Dock.Top);
        var title = new TextBlock
        {
            Text = $"管理「{target.Name}」分类",
            FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("Foreground"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(title, Dock.Left);
        var closeBtn = new Button { Content = "✕", Padding = new Thickness(8, 2, 8, 2) };
        closeBtn.Click += (_, _) => { DialogResult = false; };
        DockPanel.SetDock(closeBtn, Dock.Right);
        titleBar.Children.Add(title);
        titleBar.Children.Add(closeBtn);
        // 标题栏可拖动
        titleBar.MouseLeftButtonDown += (_, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };
        root.Children.Add(titleBar);

        // 顶部说明
        var hint = new TextBlock
        {
            Text = "勾选要放进本分类的图标,取消勾选则从本分类移除。\n一个图标同一时刻只属于一个分类。",
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = (Brush)Application.Current.FindResource("ForegroundDim"),
            TextWrapping = TextWrapping.Wrap,
        };
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);

        // 工具条
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var selAll  = new Button { Content = "全选",  Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        var selNone = new Button { Content = "全不选", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        var invert  = new Button { Content = "反选",  Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 8, 0) };
        var removeAll = new Button { Content = "移除已选", Padding = new Thickness(10, 4, 10, 4) };
        tools.Children.Add(selAll);
        tools.Children.Add(selNone);
        tools.Children.Add(invert);
        tools.Children.Add(removeAll);
        DockPanel.SetDock(tools, Dock.Top);
        root.Children.Add(tools);

        selAll.Click += (_, _) => { foreach (var r in _rows) r.cb.IsChecked = true; };
        selNone.Click += (_, _) => { foreach (var r in _rows) r.cb.IsChecked = false; };
        invert.Click += (_, _) => { foreach (var r in _rows) r.cb.IsChecked = !(r.cb.IsChecked == true); };
        removeAll.Click += (_, _) => { foreach (var r in _rows) r.cb.IsChecked = false; };

        // 底部按钮
        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var ok = new Button { Content = "保存", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "取消", Width = 80, IsCancel = true };
        ok.Click += (_, _) => { Apply(); DialogResult = true; };
        bottom.Children.Add(ok);
        bottom.Children.Add(cancel);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        // 列表
        var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var list = new StackPanel();
        sv.Content = list;
        root.Children.Add(sv);

        BuildRows(list);

        Content = outer;
    }

    private void BuildRows(StackPanel list)
    {
        var cfg = _host.Config;
        var all = _host.Binder.Scan();
        // 当前归属表(文件名 → 分类)
        var ownerMap = new Dictionary<string, CategoryConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cfg.Categories)
            foreach (var fn in c.Files)
                ownerMap[fn] = c;

        foreach (var item in all)
        {
            ownerMap.TryGetValue(item.FileName, out var owner);

            var cb = new CheckBox
            {
                IsChecked = owner == _target,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            var img = new Image
            {
                Width = 24, Height = 24,
                Source = IconExtractor.Extract(item.FullPath, jumbo: false),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var nameTb = new TextBlock
            {
                Text = item.DisplayName,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.FindResource("Foreground"),
            };
            // 归属角标
            var ownerTag = new TextBlock
            {
                Text = owner is null ? "(未分类)" : owner == _target ? "" : $"现属于「{owner.Name}」",
                Foreground = (Brush)Application.Current.FindResource("ForegroundDim"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };

            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4), LastChildFill = false };
            DockPanel.SetDock(cb, Dock.Left);
            DockPanel.SetDock(img, Dock.Left);
            DockPanel.SetDock(nameTb, Dock.Left);
            DockPanel.SetDock(ownerTag, Dock.Right);
            row.Children.Add(cb);
            row.Children.Add(img);
            row.Children.Add(nameTb);
            row.Children.Add(ownerTag);

            // 点行的任意位置都切换勾选(更顺手)
            row.MouseLeftButtonUp += (_, _) => cb.IsChecked = !(cb.IsChecked == true);

            list.Children.Add(row);
            _rows.Add((item, cb, owner));
        }

        if (_rows.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "绑定文件夹里还没有任何 .lnk / .exe。\n把快捷方式或可执行文件放进去再回来。",
                Foreground = (Brush)Application.Current.FindResource("ForegroundDim"),
                Margin = new Thickness(8, 16, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private void Apply()
    {
        var cfg = _host.Config;
        foreach (var (item, cb, _) in _rows)
        {
            var checkedNow = cb.IsChecked == true;
            // 不管原来在哪,统一先从所有分类里抹掉
            foreach (var c in cfg.Categories)
                c.Files.RemoveAll(f => f.Equals(item.FileName, StringComparison.OrdinalIgnoreCase));
            // 勾上的进本分类;没勾的 = 从本分类移除(若它原本属其他分类,放回原分类避免误删)
            if (checkedNow)
            {
                _target.Files.Add(item.FileName);
            }
            else
            {
                // 行的 owner 是打开对话框那一刻的归属
                var origin = _rows.First(r => r.item == item).owner;
                if (origin is not null && origin != _target)
                    origin.Files.Add(item.FileName);
                // 否则:没勾且原本就属本分类 → 这就是「移除」,什么也不做
            }
        }
        _host.SaveConfig();
    }
}
