using System;
using System.Collections.Generic;

namespace DockBar.Services;

/// <summary>停靠位置(三选一,需求 2.2)。</summary>
public enum DockSide { Top, Left, Right }

/// <summary>缩回模式(需求 2.1.2)。</summary>
public enum HideMode
{
    /// <summary>移出窗口区域延时自动缩回。</summary>
    OnMouseLeave,
    /// <summary>点击启动后立即缩回。</summary>
    OnLaunch
}

public sealed class CategoryConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "默认";
    /// <summary>这个分类下,绑定文件夹中文件名(不含路径)的有序列表。</summary>
    public List<string> Files { get; set; } = new();
}

/// <summary>
/// 全部用户配置。一切都序列化成 JSON 落盘到 %APPDATA%\DockBar\config.json。
/// 严格按需求 3.3:停靠位置、伸缩模式、分类、绑定文件夹全部持久化。
/// </summary>
public sealed class AppConfig
{
    public string? FolderPath { get; set; }
    public DockSide Dock { get; set; } = DockSide.Top;
    public HideMode Hide { get; set; } = HideMode.OnMouseLeave;
    public int HideDelayMs { get; set; } = 600;
    public int TriggerThicknessPx { get; set; } = 6;     // 触发条厚度
    public int TriggerLengthPx { get; set; } = 240;     // 触发条长度
    public double TriggerOffsetRatio { get; set; } = 0.5; // 触发条沿停靠边的位置 0~1
    public int WindowLengthPx { get; set; } = 720;     // 展开窗口的「长边」
    public int WindowDepthPx { get; set; } = 360;     // 展开窗口的「短边」
    public int IconSizePx { get; set; } = 48;
    public bool DarkMode { get; set; } = true;
    public List<CategoryConfig> Categories { get; set; } = new();
    /// <summary>当前选中的分类 Id。</summary>
    public string? CurrentCategoryId { get; set; }
}
