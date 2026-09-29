using CommunityToolkit.Mvvm.ComponentModel;

namespace Rebornix.Models;

public enum LogLevel { Debug, Info, Success, Warning, Error }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss");
}

public enum ItemStatus { Pending, Running, Success, AlreadyInstalled, Skipped, Failed, DryRun }

/// <summary>Geri yükleme ve kurulum listelerinde canlı durum gösteren genel satır.</summary>
public partial class StatusItem : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private ItemStatus _status = ItemStatus.Pending;
    [ObservableProperty] private string _message = "";

    public string Key { get; init; } = "";
    public object? Tag { get; init; }
}

public enum ConflictPolicy { KeepNewer, UseBackup, KeepExisting }

/// <summary>Yedek kökündeki özet bilgi (Data\backup_manifest.json).</summary>
public sealed class BackupManifest
{
    public DateTime CreatedUtc { get; set; }
    public string ComputerName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string UserProfile { get; set; } = "";
    public string WindowsVersion { get; set; } = "";
    public string AppVersion { get; set; } = "";
    public int DriverCount { get; set; }
    public int WifiCount { get; set; }
    public int AppCount { get; set; }
    public int ManualAppCount { get; set; }
    public int GameCount { get; set; }
    public int CustomFolderCount { get; set; }
    public List<string> SettingsItems { get; set; } = [];
    public long TotalBytes { get; set; }
    public List<string> Errors { get; set; } = [];
    public bool DryRun { get; set; }
}

/// <summary>Geri yükleme ilerlemesi; yeniden başlatma sonrası kaldığı yerden devam için.</summary>
public sealed class RestoreProgress
{
    public Dictionary<string, ItemStatus> Drivers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ItemStatus> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool DriversDone { get; set; }
    public bool WifiDone { get; set; }
    public bool AppsDone { get; set; }
    public bool SavesDone { get; set; }
    public bool SettingsDone { get; set; }
    public bool RebootRequired { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class ManualApp
{
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
}

public sealed class WingetPackageRef
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "winget";
}
