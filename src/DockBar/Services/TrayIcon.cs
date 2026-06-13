using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DockBar.UI;

namespace DockBar.Services;

/// <summary>
/// 系统托盘。用 WinForms 的 NotifyIcon,内存代价 ≈ 0,生态成熟。
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _ni;
    private readonly AppHost _ctx;

    public TrayIcon(AppHost ctx)
    {
        _ctx = ctx;
        _ni = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "DockBar — 悬浮收纳",
            Visible = true,
        };
        _ni.MouseDoubleClick += (_, _) => SettingsWindow.OpenOrFocus(_ctx);
        _ni.ContextMenuStrip = BuildMenu();
    }

    private ContextMenuStrip BuildMenu()
    {
        var m = new ContextMenuStrip();
        m.Items.Add("展开/收起", null, (_, _) =>
        {
            if (System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault() is { IsVisible: true })
                _ctx.HideMain();
            else
                _ctx.ShowMain();
        });
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("设置...", null, (_, _) => SettingsWindow.OpenOrFocus(_ctx));

        var auto = new ToolStripMenuItem("开机自启") { Checked = AutoStart.IsEnabled, CheckOnClick = true };
        auto.CheckedChanged += (_, _) => AutoStart.SetEnabled(auto.Checked);
        m.Items.Add(auto);

        m.Items.Add("重启", null, (_, _) =>
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
            _ctx.Quit();
        });
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("退出", null, (_, _) => _ctx.Quit());
        return m;
    }

    private static Icon LoadIcon()
    {
        // 系统自带的应用图标,免去带资源
        try
        {
            var sys = SystemIcons.Application;
            return sys;
        }
        catch { return SystemIcons.Application; }
    }

    public void Dispose()
    {
        _ni.Visible = false;
        _ni.Dispose();
    }
}

internal static class LinqShim
{
    // System.Linq 已 using;占位避免文件结尾找不到类型
}
