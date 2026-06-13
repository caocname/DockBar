// 全局 using 别名:消除 WPF / WinForms / System.Drawing 之间的同名歧义。
// 我们用 WPF 做 UI,WinForms 只用 NotifyIcon。所有重名一律默认 → WPF/Input。

global using Application       = System.Windows.Application;
global using MessageBox        = System.Windows.MessageBox;
global using Clipboard         = System.Windows.Clipboard;
global using DataFormats       = System.Windows.DataFormats;
global using DragEventArgs     = System.Windows.DragEventArgs;
global using DragDropEffects   = System.Windows.DragDropEffects;
global using DragDrop          = System.Windows.DragDrop;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using VerticalAlignment   = System.Windows.VerticalAlignment;

global using MouseEventArgs       = System.Windows.Input.MouseEventArgs;
global using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
global using MouseButton          = System.Windows.Input.MouseButton;
global using MouseButtonState     = System.Windows.Input.MouseButtonState;
global using Cursors              = System.Windows.Input.Cursors;

global using Button   = System.Windows.Controls.Button;
global using TextBox  = System.Windows.Controls.TextBox;
global using CheckBox = System.Windows.Controls.CheckBox;
global using Image    = System.Windows.Controls.Image;
global using Orientation = System.Windows.Controls.Orientation;

global using Brush   = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Color   = System.Windows.Media.Color;
global using Pen     = System.Windows.Media.Pen;

// System.AppContext 是 .NET 自带类,我们的全局上下文叫 AppHost,见 Services/AppHost.cs
