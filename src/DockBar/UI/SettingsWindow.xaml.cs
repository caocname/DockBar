using System.Globalization;
using System.Linq;
using System.Windows;
using DockBar.Native;
using DockBar.Services;

namespace DockBar.UI;

public partial class SettingsWindow : Window
{
    private readonly AppHost _ctx;
    private static SettingsWindow? _opened;

    public SettingsWindow(AppHost ctx)
    {
        _ctx = ctx;
        InitializeComponent();
        SourceInitialized += (_, _) => WindowEffects.ApplyAcrylic(this, dark: ctx.Config.DarkMode);
        LoadFromConfig();
        BrowseBtn.Click += (_, _) =>
        {
            // .NET 8 自带 Microsoft.Win32.OpenFolderDialog
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择收纳文件夹",
                InitialDirectory = FolderBox.Text,
            };
            if (dlg.ShowDialog(this) == true)
                FolderBox.Text = dlg.FolderName;
        };
        // 注意:此窗口用 Show() 非模态弹出,不能赋值 DialogResult,否则 WPF 会抛 InvalidOperationException
        // 进而导致整个 App 退出。直接 Close() 即可。
        SaveBtn.Click += (_, _) =>
        {
            try { SaveToConfig(); }
            catch (System.Exception ex)
            {
                MessageBox.Show($"保存失败:{ex.Message}", "DockBar", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Close();
        };
        CancelBtn.Click += (_, _) => Close();
        Closed += (_, _) => _opened = null;
    }

    private void LoadFromConfig()
    {
        var c = _ctx.Config;
        FolderBox.Text = c.FolderPath ?? "";
        DockTop.IsChecked = c.Dock == DockSide.Top;
        DockLeft.IsChecked = c.Dock == DockSide.Left;
        DockRight.IsChecked = c.Dock == DockSide.Right;
        HideMouseLeave.IsChecked = c.Hide == HideMode.OnMouseLeave;
        HideOnLaunch.IsChecked = c.Hide == HideMode.OnLaunch;
        DelayBox.Text = c.HideDelayMs.ToString(CultureInfo.InvariantCulture);
        IconSlider.Value = c.IconSizePx;
        AcrylicChk.IsChecked = c.UseAcrylic;
        DarkChk.IsChecked = c.DarkMode;
        AutoChk.IsChecked = AutoStart.IsEnabled;
    }

    private void SaveToConfig()
    {
        var c = _ctx.Config;
        var oldFolder = c.FolderPath;
        var oldDock = c.Dock;
        var oldDark = c.DarkMode;
        var oldAcrylic = c.UseAcrylic;
        var oldIcon = c.IconSizePx;

        c.FolderPath = string.IsNullOrWhiteSpace(FolderBox.Text) ? null : FolderBox.Text;
        c.Dock = DockTop.IsChecked == true ? DockSide.Top
               : DockLeft.IsChecked == true ? DockSide.Left
               : DockSide.Right;
        c.Hide = HideOnLaunch.IsChecked == true ? HideMode.OnLaunch : HideMode.OnMouseLeave;
        if (int.TryParse(DelayBox.Text, out var ms) && ms >= 0 && ms <= 5000) c.HideDelayMs = ms;
        c.IconSizePx = (int)IconSlider.Value;
        c.UseAcrylic = AcrylicChk.IsChecked == true;
        c.DarkMode   = DarkChk.IsChecked == true;

        // 自启注册表写入跑在后台线程,避免阻塞 UI
        var autoNew = AutoChk.IsChecked == true;
        System.Threading.Tasks.Task.Run(() => AutoStart.SetEnabled(autoNew));

        // 落盘也异步(JSON 序列化 + 磁盘 IO,主线程没必要等)
        System.Threading.Tasks.Task.Run(_ctx.SaveConfig);

        // 决定要不要重建 UI
        bool folderChanged = oldFolder != c.FolderPath;
        bool dockChanged   = oldDock   != c.Dock;
        bool themeChanged  = oldDark   != c.DarkMode || oldAcrylic != c.UseAcrylic;
        bool iconChanged   = oldIcon   != c.IconSizePx;

        if (folderChanged) _ctx.OnFolderChangedNoSave(c.FolderPath ?? "");
        if (dockChanged)   _ctx.OnDockChangedNoSave();
        if (themeChanged)  _ctx.OnThemeChanged();
        if (iconChanged && !folderChanged && !dockChanged) _ctx.OnIconSizeChanged();
    }

    public static void OpenOrFocus(AppHost ctx)
    {
        if (_opened is { IsVisible: true })
        {
            _opened.Activate();
            return;
        }
        _opened = new SettingsWindow(ctx);
        _opened.Show();
    }
}
