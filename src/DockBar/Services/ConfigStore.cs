using System;
using System.IO;
using System.Text.Json;

namespace DockBar.Services;

internal static class ConfigStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DockBar");
    private static readonly string Path_ = Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(Path_))
            {
                var json = File.ReadAllText(Path_);
                return JsonSerializer.Deserialize<AppConfig>(json, Opts) ?? new AppConfig();
            }
        }
        catch { /* 配置坏了就当新装 */ }
        return new AppConfig();
    }

    private static readonly object _saveLock = new();

    public static void Save(AppConfig cfg)
    {
        // 序列化要在锁外做(免得长占锁),写盘在锁内
        string json;
        try { json = JsonSerializer.Serialize(cfg, Opts); }
        catch { return; }
        lock (_saveLock)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Path_, json);
            }
            catch { /* 写不进就算了,下次再写 */ }
        }
    }
}
