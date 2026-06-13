using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DockBar.Native;
using DockBar.Services;
using static DockBar.Native.NativeMethods;

namespace DockBar.UI;

public partial class TriggerWindow : Window
{
    private bool _suppressed;

    public TriggerWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        MouseEnter += (_, _) =>
        {
            if (!_suppressed) AppHost.Current?.ShowMain();
        };
        // 拖拽悬停也算触发(从桌面拖图标过来的场景)
        DragEnter += (_, e) =>
        {
            if (!_suppressed) AppHost.Current?.ShowMain();
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        };
        DragOver += (_, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
        // 拖拽改停靠位置 + 中间长按
        MouseLeftButtonDown += OnDragStart;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // 不要出现在任务栏/Alt+Tab,不要抢焦点
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        WindowEffects.ApplyBlur(this);
    }

    /// <summary>根据当前 Config 把触发条贴到屏幕边沿。</summary>
    public void ApplyDock()
    {
        var cfg = AppHost.Current!.Config;
        var work = SystemParameters.WorkArea; // DPI 已折算
        switch (cfg.Dock)
        {
            case DockSide.Top:
                Width  = cfg.TriggerLengthPx;
                Height = cfg.TriggerThicknessPx;
                Left = work.Left + (work.Width - Width) * cfg.TriggerOffsetRatio;
                Top  = work.Top;
                break;
            case DockSide.Left:
                Width  = cfg.TriggerThicknessPx;
                Height = cfg.TriggerLengthPx;
                Left = work.Left;
                Top  = work.Top + (work.Height - Height) * cfg.TriggerOffsetRatio;
                break;
            case DockSide.Right:
                Width  = cfg.TriggerThicknessPx;
                Height = cfg.TriggerLengthPx;
                Left = work.Right - Width;
                Top  = work.Top + (work.Height - Height) * cfg.TriggerOffsetRatio;
                break;
        }
    }

    public void SetSuppressed(bool suppress)
    {
        if (_suppressed == suppress) return;
        _suppressed = suppress;
        Visibility = suppress ? Visibility.Collapsed : Visibility.Visible;
    }

    // ----- 拖拽改停靠 -----
    private bool _dragging;
    private System.Windows.Point _startMouse;

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _startMouse = PointToScreen(e.GetPosition(this));
        CaptureMouse();
        MouseMove += OnDragging;
        MouseLeftButtonUp += OnDragEnd;
    }

    private void OnDragging(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = PointToScreen(e.GetPosition(this));
        // 拖出 8px 才算开始
        if (Math.Abs(p.X - _startMouse.X) < 8 && Math.Abs(p.Y - _startMouse.Y) < 8) return;

        var work = SystemParameters.WorkArea;
        var cfg = AppHost.Current!.Config;

        // 离哪条边最近就贴哪条
        double dT = p.Y - work.Top;
        double dL = p.X - work.Left;
        double dR = work.Right - p.X;
        var min = Math.Min(dT, Math.Min(dL, dR));

        DockSide newSide = cfg.Dock;
        if (min == dT) newSide = DockSide.Top;
        else if (min == dL) newSide = DockSide.Left;
        else if (min == dR) newSide = DockSide.Right;

        if (newSide != cfg.Dock)
        {
            cfg.Dock = newSide;
            // 沿边的位置直接用鼠标当前位置
            cfg.TriggerOffsetRatio = newSide switch
            {
                DockSide.Top => Math.Clamp((p.X - work.Left) / Math.Max(1, work.Width  - cfg.TriggerLengthPx), 0, 1),
                _            => Math.Clamp((p.Y - work.Top)  / Math.Max(1, work.Height - cfg.TriggerLengthPx), 0, 1),
            };
            AppHost.Current!.OnDockChanged();
        }
        else
        {
            // 同一条边内沿着滑动
            cfg.TriggerOffsetRatio = newSide switch
            {
                DockSide.Top => Math.Clamp((p.X - work.Left) / Math.Max(1, work.Width  - cfg.TriggerLengthPx), 0, 1),
                _            => Math.Clamp((p.Y - work.Top)  / Math.Max(1, work.Height - cfg.TriggerLengthPx), 0, 1),
            };
            ApplyDock();
        }
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
        MouseMove -= OnDragging;
        MouseLeftButtonUp -= OnDragEnd;
        AppHost.Current?.SaveConfig();
    }
}
