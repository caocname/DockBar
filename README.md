# VoidTidy — Windows 11 悬浮快捷收纳启动栏

[![build](https://github.com/caocname/DockBar/actions/workflows/build.yml/badge.svg)](https://github.com/caocname/DockBar/actions/workflows/build.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
![.NET Framework 4.8](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?logo=dotnet)
![Windows 11](https://img.shields.io/badge/Windows-11-0078D6?logo=windows11)

> 极简、纯本地、Win11 桌面悬浮启动器。
> 鼠标移到屏幕边缘的小条上 → 自动展开图标网格 → 双击启动。
> 不联网、不打广告、不带「全家桶」。

按 [Windows11 悬浮快捷收纳启动栏软件需求说明书.md](Windows11%20悬浮快捷收纳启动栏软件需求说明书.md) 实现。
基于 **C# + WPF + .NET Framework 4.8**,Costura.Fody 单文件打包。

> **关于"VoidTidy"和"DockBar"两套命名**:产品对外名 / exe 名 / 任务管理器进程名 / 窗口标题 都已经是 **VoidTidy**;
> 仓库、解决方案、命名空间、`%APPDATA%\DockBar\` 配置目录这些**内部代号**仍叫 DockBar。
> 改后者会让旧用户配置丢失,留着不会影响使用。

## 已实现功能

| 需求 | 实现 |
|------|------|
| 2.1 悬浮触发弹出 / 自动缩回 | 顶部/左/右触发条;鼠标进入展开,离开延时缩回;支持「启动后立即缩回」模式 |
| 2.1 全屏不显示 | 4Hz 检测前台窗口是否独占当前显示器,全屏时自动隐藏触发条 |
| 2.2 自由拖动停靠 + 位置记忆 | 拖动触发条到屏幕任一边即贴边;沿边滑动调整位置;落盘到 `%APPDATA%\DockBar\config.json` |
| 2.3 绑定文件夹 + 实时同步 + 仅识别 lnk/exe | `FileSystemWatcher` 监听,200ms 去抖刷新;扩展名白名单仅 `.lnk` `.exe` |
| 2.4 自定义分栏 / 重命名 / 删除 / 拖拽分配 | 顶部标签栏,「+」按钮新增;右键标签重命名/删除;**拖图标到标签即换分类** |
| 2.5.1 双击启动 | `ShellExecute` 启动 |
| 2.5.2 右键 = 系统原生菜单 | 调用 Shell 的 `IContextMenu`,与资源管理器右键完全一致 |
| 2.5.3 拖拽导入 | 把桌面/资源管理器上的 lnk/exe 直接拖进窗口,自动复制到绑定文件夹并归入当前分类 |
| 2.5.4 拖出到桌面 | 从窗口里把图标拖到桌面 = `File.Move` 到桌面目录 |
| 2.5.5 同分类内排序 | 拖图标到另一个图标的左/右半边即在前/后插入,实时高亮指示线 |
| 3.2 Win11 视觉 | DWM 系统级圆角 + 暗色标题区 + 滚动条/Tab 精修 |
| 3.2 高分屏 | manifest 配 `PerMonitorV2` |
| 3.3 系统托盘 + 开机自启 + 配置文件 | NotifyIcon 菜单含「设置/重启/退出/开机自启」;HKCU\Run 写入 |
| 4 禁开发功能 | 无联网代码、无更新代码、扩展名白名单过滤非程序文件 |

## 资源占用

- **单文件 exe ≈ 651 KB**(Costura.Fody 编译时把 System.Text.Json 等依赖 dll 嵌入主 exe)
- 后台闲置:WorkingSet 启动后 ~80 MB,10 秒后 `EmptyWorkingSet` 释放回 OS,任务管理器 RAM 列长期 1-3 MB
- CPU:闲置 0%,4Hz 全屏 / 鼠标位置兜底检测 < 0.1%
- 文件监控:由内核 `FileSystemWatcher` 驱动,无轮询

## 下载使用

去 [Releases 页](https://github.com/caocname/DockBar/releases) 下 `VoidTidy.exe`,**双击即用**。
Win10 1903+ / 全部 Win11 自带 .NET Framework 4.8,不需要装运行时。

首次启动:右键托盘图标 → **设置** → 选一个本地文件夹作为收纳根目录。
往里丢一些 `.lnk` / `.exe`,鼠标移到屏幕顶部那条小条就会展开。

## 自己编译

需要 .NET 8 SDK(用作构建工具,目标框架仍是 net48,不影响产物)。

```bat
:: 直接跑(开发期)
dotnet run --project src\DockBar\DockBar.csproj

:: 单文件发布
dotnet publish src\DockBar\DockBar.csproj -c Release
:: 产物:src\DockBar\publish\VoidTidy.exe
```

## 配置文件

位置:`%APPDATA%\DockBar\config.json`

可以手动改这些字段(关掉程序后改):

```jsonc
{
  "folderPath": "C:\\Users\\xxx\\Desktop\\我的快捷方式",
  "dock": "Top",            // Top / Left / Right
  "hide": "OnMouseLeave",   // OnMouseLeave / OnLaunch
  "hideDelayMs": 600,
  "triggerThicknessPx": 6,  // 触发条粗细
  "triggerLengthPx": 240,   // 触发条长度
  "triggerOffsetRatio": 0.5,// 沿边的位置 0~1
  "windowLengthPx": 720,    // 展开窗口长边
  "windowDepthPx": 360,     // 展开窗口短边
  "iconSizePx": 48,
  "darkMode": true,
  "categories": [
    { "id": "...", "name": "默认", "files": ["VSCode.lnk", "Chrome.lnk"] }
  ],
  "currentCategoryId": "..."
}
```

## 关键技术决策

- **net48 而非 .NET 8**:Win11 24H2 的 ucrt 跟 .NET 8 单文件 self-extract 有 GS cookie fail-fast 兼容性问题,
  且 .NET 8 self-contained 单文件要 ~178 MB。换回 net48 + Costura.Fody 后 651 KB 单文件、内存少一半,
  Win10 1903+ / Win11 都自带运行时不需要装。
- **窗口内拖拽不走 OLE**:Win11 24H2 桌面 Shell 的 `IDropTarget` 拒绝 WPF DataObject(光标禁止符)。
  改用 `Border.CaptureMouse` + `MouseMove` + `WindowFromPoint` 自管,松手时按光标位置分发到
  排序 / 换分类 / `File.Move` 到桌面。彻底规避 OLE 兼容性。
- **不开 `AllowsTransparency`**:WPF 透明窗口在 OLE 拖入时 hit-test 会漏到下层 HWND,文件被桌面接走。
  改实色背景 + DWM 系统级圆角(`DWMWA_WINDOW_CORNER_PREFERENCE`)拿回 Win11 视觉。
  亚克力 / Mica 支持也一并删掉:实色窗口在所有桌面背景下文字都可读,而且不再受 layered window 一系列副作用影响。
- **托盘图标走嵌入资源**:`<EmbeddedResource Include="app.ico" LogicalName="DockBar.app.ico" />`,
  `Icon.ExtractAssociatedIcon` 在 net48 + 中文路径下时灵时不灵。

## 项目结构

```
DockBar.sln
src/DockBar/
├── DockBar.csproj             # AssemblyName=VoidTidy, Costura.Fody 嵌入
├── FodyWeavers.xml            # Costura 配置
├── app.manifest               # PerMonitorV2 + asInvoker
├── app.ico                    # 多分辨率 ico,从 logo.png 生成
├── App.xaml / App.xaml.cs     # 单实例 + 启动入口 + DisplayName 计数
├── Themes/Theme.xaml          # Win11 风格 brush + Tab/ScrollBar 模板
├── Native/
│   ├── NativeMethods.cs       # Win32 P/Invoke 集中地
│   ├── FullscreenDetector.cs  # 前台窗口全屏检测
│   ├── IconExtractor.cs       # SHGetFileInfo + 256px Jumbo
│   ├── ShellContextMenu.cs    # IContextMenu 原生右键菜单
│   └── WindowEffects.cs       # DWM 圆角 + 暗色标题
├── Services/
│   ├── AppConfig.cs           # 配置数据结构
│   ├── ConfigStore.cs         # JSON 落盘
│   ├── FolderBinder.cs        # FileSystemWatcher + 扫描
│   ├── AutoStart.cs           # HKCU\Run 注册
│   ├── MathPolyfill.cs        # net48 没有 Math.Clamp,自己补
│   ├── TrayIcon.cs            # NotifyIcon
│   └── AppHost.cs             # 全局上下文 / 调度
└── UI/
    ├── TriggerWindow.xaml(.cs)# 屏幕边缘触发条
    ├── MainWindow.xaml(.cs)   # 分栏 + 图标网格 + 自管拖拽
    ├── SettingsWindow.xaml(.cs)
    ├── IconPickerWindow.cs    # "管理当前分类" 多选
    └── InputBoxWindow.cs      # 单行输入对话框
tools/
└── make-ico.ps1               # logo.png → app.ico (多分辨率)
```
