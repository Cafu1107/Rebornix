using CommunityToolkit.Mvvm.ComponentModel;
using Rebornix.Helpers;

namespace Rebornix.Models;

public enum DriverCategory { Network = 1, Display = 2, Audio = 3, Chipset = 4, Bluetooth = 5, Usb = 6, Other = 7 }

public static class DriverCategories
{
    public static DriverCategory FromClass(string? className) => (className ?? "").ToUpperInvariant() switch
    {
        "NET" => DriverCategory.Network,
        "DISPLAY" => DriverCategory.Display,
        "MEDIA" or "AUDIOENDPOINT" or "AUDIOPROCESSINGOBJECT" => DriverCategory.Audio,
        "SYSTEM" or "HDC" or "PROCESSOR" or "SCSIADAPTER" or "SECURITYDEVICES" or "SMARTCARDREADER" => DriverCategory.Chipset,
        "BLUETOOTH" => DriverCategory.Bluetooth,
        "USB" or "USBDEVICE" => DriverCategory.Usb,
        _ => DriverCategory.Other
    };

    public static string DisplayName(DriverCategory c) => Loc.Get("DriverCat_" + c);

    /// <summary>Yedek klasöründeki alt klasör adı (sıralı, ağ ilk).</summary>
    public static string FolderName(DriverCategory c) => $"{(int)c:00}_{c}";
}

/// <summary>Sistemde yüklü üçüncü parti sürücü paketi (Get-WindowsDriver çıktısı).</summary>
public sealed class DriverInfo
{
    public string PublishedName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string InfName { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string ClassDescription { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Version { get; set; } = "";
    public string Date { get; set; } = "";
    public long SizeBytes { get; set; }
    public string[] Devices { get; set; } = [];
}

public partial class DriverItem : ObservableObject
{
    public DriverItem(DriverInfo info)
    {
        Info = info;
        Category = DriverCategories.FromClass(info.ClassName);
    }

    public DriverInfo Info { get; }
    public DriverCategory Category { get; }
    public string CategoryName => DriverCategories.DisplayName(Category);
    public bool IsCritical => Category == DriverCategory.Network;
    public string DisplayName => Info.Devices.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)) ?? Info.InfName;
    public string SizeText => SafePath.FormatBytes(Info.SizeBytes);

    [ObservableProperty] private bool _isSelected = true;
}

/// <summary>Yedek içindeki Drivers\drivers.json kaydı.</summary>
public sealed class DriverManifestEntry
{
    public string DisplayName { get; set; } = "";
    public string FolderName { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string InfName { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Version { get; set; } = "";
    public string Date { get; set; } = "";
    public string ClassName { get; set; } = "";
    public DriverCategory Category { get; set; }
    public long SizeBytes { get; set; }
}
