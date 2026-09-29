using Rebornix.Helpers;

namespace Rebornix.Services;

/// <summary>
/// Uygulamanın kendi dosyaları (ayar, log, araçlar) HER ZAMAN exe'nin yanında tutulur.
/// AppData veya kayıt defterine uygulama ayarı yazılmaz.
/// </summary>
public static class AppPaths
{
    public static string BaseDir { get; private set; } = SafePath.Normalize(AppContext.BaseDirectory);

    /// <summary>Testlerde exe klasörü yerine geçici klasör kullanmak için.</summary>
    internal static void OverrideBase(string dir) => BaseDir = SafePath.Normalize(dir);

    public static string DataDir => Path.Combine(BaseDir, "Data");
    public static string LogsDir => Path.Combine(BaseDir, "Logs");
    public static string ToolsDir => Path.Combine(BaseDir, "Tools");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string CatalogFile => Path.Combine(DataDir, "katalog.json");
    public static string ProfilesFile => Path.Combine(DataDir, "profiller.json");

    public static string? ExePath => Environment.ProcessPath;

    public static void EnsureBaseFolders()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(ToolsDir);
    }
}

/// <summary>Bir yedek kökünün (ör. D:\Rebornix) klasör düzeni.</summary>
public sealed class BackupLayout
{
    public BackupLayout(string root) => Root = SafePath.Normalize(root);

    public string Root { get; }
    public string DriversDir => Path.Combine(Root, "Drivers");
    public string DriversManifest => Path.Combine(DriversDir, "drivers.json");
    public string WifiDir => Path.Combine(Root, "WiFi");
    public string WifiFile => Path.Combine(WifiDir, "wifi.rbxenc");
    public string DataDir => Path.Combine(Root, "Data");
    public string ManifestFile => Path.Combine(DataDir, "backup_manifest.json");
    public string AppsFile => Path.Combine(DataDir, "winget_apps.json");
    public string ManualAppsFile => Path.Combine(DataDir, "elle_kurulacaklar.json");
    public string AllProgramsFile => Path.Combine(DataDir, "tum_programlar.json");
    public string ProgressFile => Path.Combine(DataDir, "restore_progress.json");
    public string BackupsDir => Path.Combine(Root, "Backups");
    public string LudusaviDir => Path.Combine(BackupsDir, "Ludusavi");
    public string LudusaviInfoFile => Path.Combine(BackupsDir, "ludusavi_info.json");
    public string CustomDir => Path.Combine(BackupsDir, "Custom");
    public string CustomManifest => Path.Combine(CustomDir, "custom_folders.json");
    public string WindowsSettingsDir => Path.Combine(Root, "WindowsSettings");
    public string WindowsSettingsFile => Path.Combine(WindowsSettingsDir, "settings.json");
    public string SnapshotsDir => Path.Combine(WindowsSettingsDir, "_GeriAlma");

    /// <summary>Bu klasörde tanınabilir bir yedek var mı?</summary>
    public bool LooksLikeBackup()
    {
        if (File.Exists(ManifestFile)) return true;
        var hits = 0;
        if (File.Exists(DriversManifest)) hits++;
        if (File.Exists(WifiFile)) hits++;
        if (File.Exists(AppsFile)) hits++;
        if (Directory.Exists(LudusaviDir) || File.Exists(CustomManifest)) hits++;
        if (File.Exists(WindowsSettingsFile)) hits++;
        return hits >= 1;
    }
}
