# DockBar — Windows 11 悬浮快捷收纳启动栏

[![build](https://github.com/caocname/DockBar/actions/workflows/build.yml/badge.svg)](https://github.com/caocname/DockBar/actions/workflows/build.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Windows 11](https://img.shields.io/badge/Windows-11-0078D6?logo=windows11)

> 极简、纯本地、毛玻璃风格的 Win11 桌面悬浮启动器。
> 鼠标移到屏幕边缘的小条上 → 自动展开图标网格 → 双击启动。
> 不联网、不打广告、不带「全家桶」。

按 [Windows11 悬浮快捷收纳启动栏软件需求说明书.md](Windows11%20悬浮快捷收纳启动栏软件需求说明书.md) 实现。
基于 **C# + WPF + .NET 8**,单文件 self-contained 发布。

## 当前迭代(MVP-1)已实现

| 需求 | 实现 |
|------|------|
| 2.1 悬浮触发弹出 / 自动缩回 | 顶部/左/右触发条;鼠标进入触发条展开,离开延时缩回;支持「启动后立即缩回」模式 |
| 2.1 全屏不显示 | 1Hz 检测前台窗口是否独占当前显示器,全屏时自动隐藏触发条 |
| 2.2 自由拖动停靠 + 位置记忆 | 拖动触发条到屏幕任一边即贴边;沿边滑动调整位置;落盘到 `%APPDATA%\DockBar\config.json` |
| 2.3 绑定文件夹 + 实时同步 + 仅识别 lnk/exe | `FileSystemWatcher` 监听,200ms 去抖刷新;扩展名白名单仅 `.lnk` `.exe` |
| 2.4 自定义分栏 / 重命名 / 删除 / 拖拽分配 | 顶部标签栏,「+」按钮新增;右键标签重命名/删除;拖拽待补全(本轮先用「自动落入默认分类」 + 设置可后期手动改归属) |
| 2.5.1 双击启动 | `ShellExecute` 启动 |
| 2.5.2 右键 = 系统原生菜单 | 调用 Shell 的 `IContextMenu`,与资源管理器右键完全一致 |
| 2.5.3 拖拽导入 | 把桌面上的 lnk/exe 直接拖进窗口,自动复制到绑定文件夹并归入当前分类 |
| 3.2 Win11 视觉 | 圆角 + Mica/Acrylic + 暗色模式开关 |
| 3.2 高分屏 | manifest 配 `PerMonitorV2` |
| 3.3 系统托盘 + 开机自启 + 配置文件 | NotifyIcon 菜单含「设置/重启/退出/开机自启」;HKCU\Run 写入 |
| 4   禁开发功能 | 无联网代码、无更新代码、扩展名白名单过滤非程序文件 |

## 还没做(下一轮)

- 把分配到分类的操作从「右键菜单选目标分类」也加上(目前拖入新文件落入「默认」,后续支持图标拖到标签上)
- 尺寸自定义滑块(目前已能在 `config.json` 改)
- 触发灵敏度可调

## 编译运行

### 1. 安装 .NET 8 SDK(只需一次)

机器目前只有 .NET runtime,没有 SDK。从 <https://dotnet.microsoft.com/download/dotnet/8.0> 下载
**.NET 8 SDK (x64) for Windows**,一路下一步即可。

```bat
dotnet --list-sdks
:: 应能看到 8.0.xxx
```

### 2. 直接运行(开发期)

```bat
cd /d f:\桌面收纳软件开发
dotnet run --project src\DockBar\DockBar.csproj
```

启动后右键托盘图标 → **设置** → 选一个本地文件夹作为收纳根目录。
往里丢一些 `.lnk` / `.exe`,鼠标移到屏幕顶部那条小条就会展开。

### 3. 发布单文件 exe(交付)

```bat
dotnet publish src\DockBar\DockBar.csproj -c Release -r win-x64 ^
    /p:_IsPublishing=true ^
    -o publish
```

产物在 `publish\DockBar.exe`,双击直接用,不需要 .NET 运行时。

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
  "useAcrylic": true,
  "darkMode": true,
  "categories": [
    { "id": "...", "name": "默认", "files": ["VSCode.lnk", "Chrome.lnk"] }
  ],
  "currentCategoryId": "..."
}
```

## 资源占用预期

- 后台闲置:~25-35 MB 私有内存(WPF 基线;之后可换 WinUI 3 + Native AOT 进一步压到 15 MB 左右)
- CPU:闲置 0%,1Hz 全屏检测 < 0.1%
- 文件监控:由内核驱动,无轮询

## 项目结构

```
DockBar.sln
src/DockBar/
├── DockBar.csproj
├── app.manifest               # PerMonitorV2 + asInvoker
├── App.xaml / App.xaml.cs     # 单实例 + 启动入口
├── Themes/Theme.xaml          # 极简 Win11 风格
├── Native/
│   ├── NativeMethods.cs       # Win32 P/Invoke 集中地
│   ├── FullscreenDetector.cs  # 前台窗口全屏检测
│   ├── IconExtractor.cs       # SHGetFileInfo + 256px Jumbo
│   └── ShellContextMenu.cs    # IContextMenu 原生右键菜单
├── Services/
│   ├── AppConfig.cs           # 配置数据结构
│   ├── ConfigStore.cs         # JSON 落盘
│   ├── FolderBinder.cs        # FileSystemWatcher + 扫描
│   ├── AutoStart.cs           # HKCU\Run 注册
│   ├── TrayIcon.cs            # NotifyIcon
│   └── AppContext.cs          # 全局上下文 / 调度
└── UI/
    ├── TriggerWindow.xaml(.cs)# 屏幕边缘触发条
    ├── MainWindow.xaml(.cs)   # 分栏 + 图标网格
    ├── SettingsWindow.xaml(.cs)
    └── InputBoxWindow.cs      # 单行输入对话框
```
