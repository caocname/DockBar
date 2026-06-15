using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DockBar.Native;

namespace DockBar.UI;

/// <summary>
/// 极简单行输入框,替代 VB.Interaction.InputBox。
/// </summary>
public sealed class InputBoxWindow : Window
{
    private readonly TextBox _tb;

    public string? Result { get; private set; }

    public InputBoxWindow(string title, string prompt, string initial)
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        var dark = DockBar.Services.AppHost.Current?.Config.DarkMode ?? true;
        var fg = (Brush)Application.Current.FindResource(dark ? "Foreground" : "ForegroundLight");
        var fgDim = (Brush)Application.Current.FindResource(dark ? "ForegroundDim" : "ForegroundDimLight");
        var surface = (Brush)Application.Current.FindResource(dark ? "DialogSurfaceDark" : "DialogSurfaceLight");
        Foreground = fg;
        if (!dark)
        {
            Resources["Foreground"]    = fg;
            Resources["ForegroundDim"] = fgDim;
            Resources["SurfaceHover"]  = (Brush)Application.Current.FindResource("SurfaceHoverLight");
        }
        ResizeMode = ResizeMode.NoResize;
        SourceInitialized += (_, _) => WindowEffects.ApplyRoundCorners(this, dark: dark);

        var outer = new Border
        {
            Background = surface,
            CornerRadius = new CornerRadius(12),
        };
        var sp = new StackPanel { Margin = new Thickness(20) };
        outer.Child = sp;

        // 自绘标题
        var titleTb = new TextBlock
        {
            Text = title,
            FontSize = 14, FontWeight = FontWeights.SemiBold,
            Foreground = fg,
            Margin = new Thickness(0, 0, 0, 10),
        };
        sp.Children.Add(titleTb);

        sp.Children.Add(new TextBlock
        {
            Text = prompt,
            Foreground = fgDim,
            Margin = new Thickness(0, 0, 0, 8),
        });
        _tb = new TextBox { Text = initial, Foreground = fg };
        sp.Children.Add(_tb);
        var btnRow = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var ok = new Button
        {
            Content = "确定", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0),
            Style = (Style)Application.Current.FindResource("AccentButtonStyle"),
        };
        var cancel = new Button { Content = "取消", Width = 80, IsCancel = true };
        ok.Click += (_, _) => { Result = _tb.Text; DialogResult = true; };
        cancel.Click += (_, _) => DialogResult = false;
        btnRow.Children.Add(ok);
        btnRow.Children.Add(cancel);
        sp.Children.Add(btnRow);
        Content = outer;
        // 整窗可拖动
        MouseLeftButtonDown += (_, e) => { if (e.ChangedButton == MouseButton.Left && e.Source is not TextBox) DragMove(); };
        Loaded += (_, _) => { _tb.Focus(); _tb.SelectAll(); };
    }

    public static string? Ask(string title, string prompt, string initial = "")
    {
        var w = new InputBoxWindow(title, prompt, initial)
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive)
        };
        return w.ShowDialog() == true ? w.Result : null;
    }
}
