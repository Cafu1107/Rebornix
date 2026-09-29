using System.Runtime.InteropServices;
using Rebornix.Helpers;
using Microsoft.Win32;

namespace Rebornix.Services;

public enum SettingKind { Registry, Wallpaper, Regional, Environment }

public sealed record RegRef(string Key, string Name);

/// <summary>Tek bir ayar kalemi: yalnızca burada listelenen HKCU değerleri okunur/yazılır.</summary>
public sealed class SettingDefinition
{
    public required string Id { get; init; }
    public required SettingKind Kind { get; init; }
    public RegRef[] Values { get; init; } = [];
    public bool ExplorerRestart { get; init; }
    public string? Broadcast { get; init; }
    public string Title => Loc.Get("WinSet_" + Id);
    public string Description => Loc.Get("WinSet_" + Id + "_Desc");
}

public sealed class RegValueData
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Text { get; set; }
    public long? Number { get; set; }
    public string? Base64 { get; set; }
    public string[]? Multi { get; set; }

    public bool SameAs(RegValueData o) =>
        string.Equals(Kind, o.Kind, StringComparison.Ordinal) &&
        string.Equals(Text, o.Text, StringComparison.Ordinal) &&
        Number == o.Number &&
        string.Equals(Base64, o.Base64, StringComparison.Ordinal) &&
        (Multi ?? []).SequenceEqual(o.Multi ?? []);
}

public sealed class SettingCapture
{
    public string Id { get; set; } = "";
    public List<RegValueData> Values { get; set; } = [];
    public string? WallpaperFile { get; set; }
    public string? LocaleName { get; set; }

    /// <summary>Geri yükleme sırasında önceden OLMAYAN ve yeni oluşturulan değerler (geri almada silinir).</summary>
    public List<RegRef> CreatedValues { get; set; } = [];
}

public sealed class SettingsBackup
{
    public DateTime CreatedUtc { get; set; }
    public string WindowsBuild { get; set; } = "";
    public string UserProfile { get; set; } = "";
    public bool IsSnapshot { get; set; }
    public List<SettingCapture> Items { get; set; } = [];
}

public sealed class ApplyReport
{
    public List<string> Applied { get; } = [];
    public List<string> Skipped { get; } = [];
    public List<string> Failed { get; } = [];
    public bool ExplorerRestartSuggested { get; set; }
    public string? SnapshotFile { get; set; }
}

public sealed record ExcludedSetting(string Title, string Reason);

/// <summary>
/// Windows ayarlarını yedekler / geri yükler.
/// Kurallar: sadece HKCU, sadece beyaz listedeki değerler, geri yüklemeden önce otomatik anlık görüntü (geri alma).
/// </summary>
public sealed class WindowsSettingsService
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Dwm = @"Software\Microsoft\Windows\DWM";
    private const string Accent = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string Desktop = @"Control Panel\Desktop";
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ExplorerKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    private const string Search = @"Software\Microsoft\Windows\CurrentVersion\Search";
    private const string Mouse = @"Control Panel\Mouse";
    private const string Keyboard = @"Control Panel\Keyboard";
    internal const string International = @"Control Panel\International";
    internal const string EnvironmentKey = "Environment";

    private static RegRef[] R(string key, params string[] names) => names.Select(n => new RegRef(key, n)).ToArray();

    public static IReadOnlyList<SettingDefinition> Definitions { get; } =
    [
        new()
        {
            Id = "Theme", Kind = SettingKind.Registry, ExplorerRestart = true, Broadcast = "ImmersiveColorSet",
            Values =
            [
                ..R(Personalize, "AppsUseLightTheme", "SystemUsesLightTheme", "EnableTransparency", "ColorPrevalence"),
                ..R(Dwm, "AccentColor", "ColorizationColor", "ColorizationAfterglow", "ColorPrevalence", "EnableWindowColorization"),
                ..R(Accent, "AccentPalette", "AccentColorMenu", "StartColorMenu")
            ]
        },
        new()
        {
            Id = "Wallpaper", Kind = SettingKind.Wallpaper,
            Values = R(Desktop, "WallpaperStyle", "TileWallpaper")
        },
        new()
        {
            Id = "Taskbar", Kind = SettingKind.Registry, ExplorerRestart = true, Broadcast = "TraySettings",
            Values =
            [
                ..R(Advanced, "TaskbarAl", "ShowTaskViewButton", "TaskbarGlomLevel", "TaskbarSmallIcons"),
                ..R(Search, "SearchboxTaskbarMode")
            ]
        },
        new()
        {
            Id = "Explorer", Kind = SettingKind.Registry, ExplorerRestart = true,
            Values =
            [
                ..R(Advanced, "HideFileExt", "Hidden", "ShowSuperHidden", "LaunchTo", "NavPaneExpandToCurrentFolder",
                    "NavPaneShowAllFolders", "ShowStatusBar", "ShowInfoTip"),
                ..R(ExplorerKey, "ShowRecent", "ShowFrequent")
            ]
        },
        new()
        {
            Id = "MouseKeyboard", Kind = SettingKind.Registry,
            Values =
            [
                ..R(Mouse, "MouseSpeed", "MouseThreshold1", "MouseThreshold2", "MouseSensitivity", "DoubleClickSpeed",
                    "SwapMouseButtons", "MouseTrails", "SnapToDefaultButton", "MouseHoverTime"),
                ..R(Desktop, "WheelScrollLines", "WheelScrollChars"),
                ..R(Keyboard, "KeyboardDelay", "KeyboardSpeed")
            ]
        },
        new()
        {
            Id = "Regional", Kind = SettingKind.Regional, Broadcast = "intl",
            Values = R(International, "sShortDate", "sDate", "iDate", "sLongDate", "sYearMonth", "sShortTime",
                "sTimeFormat", "sTime", "iTime", "iTLZero", "iTimePrefix", "s1159", "s2359", "iFirstDayOfWeek",
                "iFirstWeekOfYear", "sDecimal", "sThousand", "sGrouping", "sList", "iMeasure", "sCurrency",
                "iCurrency", "iCurrDigits", "iNegCurr", "sMonDecimalSep", "sMonThousandSep")
        },
        new()
        {
            Id = "Environment", Kind = SettingKind.Environment, Broadcast = "Environment"
        }
    ];

    /// <summary>Değerlendirilip güvenilir bulunmadığı için arayüze KONMAYAN kalemler.</summary>
    public static IReadOnlyList<ExcludedSetting> Excluded { get; } =
    [
        new(Loc.Get("WinSetEx_DefaultApps"), Loc.Get("WinSetEx_DefaultApps_Reason")),
        new(Loc.Get("WinSetEx_PowerPlans"), Loc.Get("WinSetEx_PowerPlans_Reason")),
        new(Loc.Get("WinSetEx_Languages"), Loc.Get("WinSetEx_Languages_Reason")),
        new(Loc.Get("WinSetEx_Widgets"), Loc.Get("WinSetEx_Widgets_Reason")),
        new(Loc.Get("WinSetEx_Pins"), Loc.Get("WinSetEx_Pins_Reason")),
        new(Loc.Get("WinSetEx_Cursors"), Loc.Get("WinSetEx_Cursors_Reason")),
        new(Loc.Get("WinSetEx_TimeZone"), Loc.Get("WinSetEx_TimeZone_Reason"))
    ];

    private static readonly HashSet<string> EnvNeverTouch = new(StringComparer.OrdinalIgnoreCase)
        { "TEMP", "TMP", "OneDrive", "OneDriveConsumer", "OneDriveCommercial" };

    private readonly string? _sandbox;

    /// <param name="sandboxPrefix">
    /// Test için: tüm anahtarlar HKCU\{sandboxPrefix}\... altına yönlendirilir, sistem mesajı gönderilmez,
    /// duvar kağıdı API'si çağrılmaz. Gerçek kullanımda null.
    /// </param>
    public WindowsSettingsService(string? sandboxPrefix = null) => _sandbox = sandboxPrefix;

    private bool IsSandbox => _sandbox is not null;
    private string Map(string key) => _sandbox is null ? key : _sandbox + "\\" + key;

    public static SettingDefinition Def(string id) => Definitions.First(d => d.Id == id);

    // ───────────────────────────── Yedekleme ─────────────────────────────

    public SettingsBackup Capture(IEnumerable<string> ids, string? fileDir, bool isSnapshot = false)
    {
        var backup = new SettingsBackup
        {
            CreatedUtc = DateTime.UtcNow,
            WindowsBuild = Environment.OSVersion.Version.ToString(),
            UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            IsSnapshot = isSnapshot
        };
        foreach (var id in ids.Distinct())
        {
            var def = Definitions.FirstOrDefault(d => d.Id == id);
            if (def is null) continue;
            backup.Items.Add(CaptureOne(def, fileDir));
        }
        return backup;
    }

    private SettingCapture CaptureOne(SettingDefinition def, string? fileDir)
    {
        var c = new SettingCapture { Id = def.Id };
        if (def.Kind == SettingKind.Environment)
        {
            using var k = Registry.CurrentUser.OpenSubKey(Map(EnvironmentKey));
            if (k is not null)
            {
                foreach (var name in k.GetValueNames())
                {
                    if (EnvNeverTouch.Contains(name)) continue;
                    var v = ReadValue(EnvironmentKey, name);
                    if (v is not null) c.Values.Add(v);
                }
            }
            return c;
        }

        foreach (var r in def.Values)
        {
            var v = ReadValue(r.Key, r.Name);
            if (v is not null) c.Values.Add(v);
        }

        if (def.Kind == SettingKind.Regional)
            c.LocaleName = ReadValue(International, "LocaleName")?.Text;

        if (def.Kind == SettingKind.Wallpaper && fileDir is not null)
            c.WallpaperFile = CopyCurrentWallpaper(fileDir);

        return c;
    }

    private string? CopyCurrentWallpaper(string fileDir)
    {
        var path = ReadValue(Desktop, "WallPaper")?.Text;
        string? source = null;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) source = path;
        else
        {
            var transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Themes\TranscodedWallpaper");
            if (!IsSandbox && File.Exists(transcoded)) source = transcoded;
        }
        if (source is null) return null;

        var ext = Path.GetExtension(source);
        if (string.IsNullOrEmpty(ext) || ext.Length > 5) ext = ".jpg";
        var name = "wallpaper" + ext.ToLowerInvariant();
        Directory.CreateDirectory(fileDir);
        File.Copy(source, Path.Combine(fileDir, name), overwrite: true);
        return name;
    }

    public static void Save(SettingsBackup backup, string file) => Json.Write(file, backup);

    public static SettingsBackup? Load(string file) => Json.Read<SettingsBackup>(file);

    // ───────────────────────────── Geri yükleme ─────────────────────────────

    /// <summary>
    /// Seçilen kalemleri uygular. Önce mevcut durumun anlık görüntüsü snapshotsDir altına kaydedilir.
    /// exact=false: ortam değişkenleri birleştirilir (üzerine yazılmaz). exact=true: geri alma modu.
    /// </summary>
    public ApplyReport Apply(SettingsBackup backup, IEnumerable<string> ids, string backupFilesDir, string snapshotsDir,
        bool dryRun, bool exact = false)
    {
        var report = new ApplyReport();
        var selected = backup.Items.Where(i => ids.Contains(i.Id)).ToList();
        if (selected.Count == 0) return report;

        SettingsBackup? snapshot = null;
        string? snapshotFile = null, snapshotDir = null;
        if (!dryRun)
        {
            snapshotDir = Path.Combine(snapshotsDir, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
            snapshot = Capture(selected.Select(s => s.Id), snapshotDir, isSnapshot: true);
            snapshotFile = Path.Combine(snapshotDir, "settings.json");
            Save(snapshot, snapshotFile);
            report.SnapshotFile = snapshotFile;
            Log.Info(Loc.F("WinSet_SnapshotSaved", snapshotDir));
        }

        var broadcasts = new HashSet<string>();
        foreach (var item in selected)
        {
            var def = Definitions.FirstOrDefault(d => d.Id == item.Id);
            if (def is null) continue;
            var created = new List<RegRef>();
            try
            {
                var result = def.Kind switch
                {
                    SettingKind.Environment => ApplyEnvironment(item, backup.UserProfile, dryRun, exact, created),
                    SettingKind.Regional => ApplyRegional(item, dryRun, exact, created),
                    SettingKind.Wallpaper => ApplyWallpaper(item, backupFilesDir, dryRun, created),
                    _ => ApplyValues(item.Values, dryRun, created)
                };

                if (exact) DeleteCreated(item.CreatedValues, dryRun);

                if (result is null)
                {
                    report.Applied.Add(def.Title);
                    if (def.ExplorerRestart) report.ExplorerRestartSuggested = true;
                    if (def.Broadcast is not null) broadcasts.Add(def.Broadcast);
                }
                else report.Skipped.Add($"{def.Title}: {result}");

                if (snapshot is not null)
                {
                    var snapItem = snapshot.Items.FirstOrDefault(s => s.Id == item.Id);
                    if (snapItem is not null) snapItem.CreatedValues = created;
                }
            }
            catch (Exception ex)
            {
                report.Failed.Add($"{def.Title}: {ex.Message}");
                Log.Error(Loc.F("WinSet_ApplyFailed", def.Title), ex);
            }
        }

        if (snapshot is not null && snapshotFile is not null) Save(snapshot, snapshotFile);
        if (!dryRun && !IsSandbox)
            foreach (var b in broadcasts) Native.BroadcastSettingChange(b);
        return report;
    }

    /// <summary>Anlık görüntüye geri döner (geri alma).</summary>
    public ApplyReport Undo(string snapshotFile, string snapshotsDir, bool dryRun)
    {
        var snap = Load(snapshotFile) ?? throw new InvalidOperationException(Loc.Get("WinSet_SnapshotUnreadable"));
        var dir = Path.GetDirectoryName(snapshotFile)!;
        return Apply(snap, snap.Items.Select(i => i.Id).ToList(), dir, snapshotsDir, dryRun, exact: true);
    }

    public static List<(string File, DateTime Time, string Items)> ListSnapshots(string snapshotsDir)
    {
        var list = new List<(string, DateTime, string)>();
        if (!Directory.Exists(snapshotsDir)) return list;
        foreach (var f in Directory.EnumerateFiles(snapshotsDir, "settings.json", SearchOption.AllDirectories))
        {
            try
            {
                var s = Load(f);
                if (s is null) continue;
                list.Add((f, s.CreatedUtc.ToLocalTime(),
                    string.Join(", ", s.Items.Select(i => Definitions.FirstOrDefault(d => d.Id == i.Id)?.Title ?? i.Id))));
            }
            catch
            {
                // bozuk anlık görüntü atlanır
            }
        }
        return list.OrderByDescending(x => x.Item2).ToList();
    }

    /// <returns>null = uygulandı; aksi halde atlama sebebi.</returns>
    private string? ApplyValues(IEnumerable<RegValueData> values, bool dryRun, List<RegRef> created)
    {
        foreach (var v in values)
        {
            var current = ReadValue(v.Key, v.Name);
            if (current is not null && current.SameAs(v)) continue;
            if (dryRun)
            {
                Log.Info(Loc.F("Dry_RegSet", $@"HKCU\{v.Key}", v.Name));
                continue;
            }
            WriteValue(v);
            if (current is null) created.Add(new RegRef(v.Key, v.Name));
        }
        return null;
    }

    private string? ApplyRegional(SettingCapture item, bool dryRun, bool exact, List<RegRef> created)
    {
        if (!exact)
        {
            var current = ReadValue(International, "LocaleName")?.Text;
            if (!string.Equals(current, item.LocaleName, StringComparison.OrdinalIgnoreCase))
                return Loc.F("WinSet_LocaleMismatch", item.LocaleName ?? "?", current ?? "?");
        }
        return ApplyValues(item.Values, dryRun, created);
    }

    private string? ApplyWallpaper(SettingCapture item, string backupFilesDir, bool dryRun, List<RegRef> created)
    {
        if (string.IsNullOrEmpty(item.WallpaperFile)) return Loc.Get("WinSet_NoWallpaperFile");
        var src = SafePath.Combine(backupFilesDir, SafePath.SanitizeFileName(item.WallpaperFile));
        if (!File.Exists(src)) return Loc.Get("WinSet_NoWallpaperFile");

        var picturesDir = IsSandbox
            ? Path.Combine(Path.GetTempPath(), "RebornixTest_Pictures")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Rebornix");
        var dest = Path.Combine(picturesDir, $"duvar_kagidi_{DateTime.Now:yyyyMMdd_HHmmss}{Path.GetExtension(src)}");
        if (dryRun)
        {
            Log.Info(Loc.F("Dry_Wallpaper", dest));
            return null;
        }

        Directory.CreateDirectory(picturesDir);
        File.Copy(src, dest, overwrite: false);
        ApplyValues(item.Values, dryRun: false, created);
        if (IsSandbox)
        {
            if (ReadValue(Desktop, "WallPaper") is null) created.Add(new RegRef(Desktop, "WallPaper"));
            WriteValue(new RegValueData { Key = Desktop, Name = "WallPaper", Kind = nameof(RegistryValueKind.String), Text = dest });
        }
        else if (!Native.SetWallpaper(dest))
        {
            throw new InvalidOperationException(Loc.Get("WinSet_WallpaperApiFailed"));
        }
        return null;
    }

    private string? ApplyEnvironment(SettingCapture item, string? oldProfile, bool dryRun, bool exact, List<RegRef> created)
    {
        var newProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var kept = new List<string>();
        foreach (var v in item.Values)
        {
            if (EnvNeverTouch.Contains(v.Name)) continue;
            var current = ReadValue(EnvironmentKey, v.Name);

            if (exact)
            {
                if (current is not null && current.SameAs(v)) continue;
                if (dryRun) { Log.Info(Loc.F("Dry_EnvSet", v.Name)); continue; }
                WriteValue(v);
                if (current is null) created.Add(new RegRef(EnvironmentKey, v.Name));
                continue;
            }

            if (string.Equals(v.Name, "Path", StringComparison.OrdinalIgnoreCase))
            {
                var merged = EnvMerge.MergePath(current?.Text, v.Text, oldProfile, newProfile);
                if (string.Equals(merged, current?.Text, StringComparison.Ordinal)) continue;
                if (dryRun) { Log.Info(Loc.F("Dry_EnvMergePath", v.Name)); continue; }
                WriteValue(new RegValueData
                {
                    Key = EnvironmentKey, Name = current is null ? v.Name : current.Name,
                    Kind = current?.Kind ?? v.Kind, Text = merged
                });
                if (current is null) created.Add(new RegRef(EnvironmentKey, v.Name));
            }
            else if (current is null)
            {
                if (dryRun) { Log.Info(Loc.F("Dry_EnvSet", v.Name)); continue; }
                WriteValue(v);
                created.Add(new RegRef(EnvironmentKey, v.Name));
            }
            else if (!current.SameAs(v))
            {
                kept.Add(v.Name);
            }
        }
        if (kept.Count > 0) Log.Info(Loc.F("WinSet_EnvKept", string.Join(", ", kept)));
        return null;
    }

    private void DeleteCreated(IEnumerable<RegRef> refs, bool dryRun)
    {
        foreach (var r in refs)
        {
            if (dryRun) { Log.Info(Loc.F("Dry_RegDelete", $@"HKCU\{r.Key}", r.Name)); continue; }
            using var k = Registry.CurrentUser.OpenSubKey(Map(r.Key), writable: true);
            k?.DeleteValue(r.Name, throwOnMissingValue: false);
        }
    }

    // ───────────────────────────── Kayıt defteri erişimi ─────────────────────────────

    internal RegValueData? ReadValue(string key, string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(Map(key));
        if (k is null) return null;
        var raw = k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null) return null;
        // Kayıt defterindeki gerçek adı (büyük/küçük harf) koru
        var realName = k.GetValueNames().FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) ?? name;
        var kind = k.GetValueKind(name);
        var d = new RegValueData { Key = key, Name = realName, Kind = kind.ToString() };
        switch (kind)
        {
            case RegistryValueKind.DWord: d.Number = Convert.ToInt32(raw); break;
            case RegistryValueKind.QWord: d.Number = Convert.ToInt64(raw); break;
            case RegistryValueKind.String:
            case RegistryValueKind.ExpandString: d.Text = raw as string; break;
            case RegistryValueKind.MultiString: d.Multi = raw as string[]; break;
            case RegistryValueKind.Binary: d.Base64 = Convert.ToBase64String((byte[])raw); break;
            default: return null; // bilinmeyen türler yedeklenmez
        }
        return d;
    }

    internal void WriteValue(RegValueData v)
    {
        if (!Enum.TryParse<RegistryValueKind>(v.Kind, out var kind))
            throw new InvalidOperationException("Bilinmeyen değer türü: " + v.Kind);
        if (!IsAllowedKey(v.Key))
            throw new InvalidOperationException("İzin verilmeyen anahtar: " + v.Key);

        object data = kind switch
        {
            RegistryValueKind.DWord => unchecked((int)(v.Number ?? 0)),
            RegistryValueKind.QWord => v.Number ?? 0L,
            RegistryValueKind.String or RegistryValueKind.ExpandString => v.Text ?? "",
            RegistryValueKind.MultiString => v.Multi ?? [],
            RegistryValueKind.Binary => Convert.FromBase64String(v.Base64 ?? ""),
            _ => throw new InvalidOperationException("Desteklenmeyen tür: " + kind)
        };
        using var k = Registry.CurrentUser.CreateSubKey(Map(v.Key), writable: true);
        k.SetValue(v.Name, data, kind);
    }

    /// <summary>Yedek dosyası elle değiştirilse bile beyaz liste dışındaki anahtarlara yazılmaz.</summary>
    internal static bool IsAllowedKey(string key) =>
        key == EnvironmentKey || Definitions.Any(d => d.Values.Any(v => string.Equals(v.Key, key, StringComparison.OrdinalIgnoreCase)));

    // ───────────────────────────── Yardımcılar ─────────────────────────────

    public static void RestartExplorer()
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
        {
            try { p.Kill(); } catch { /* yoksay */ }
        }
        Thread.Sleep(1500);
        if (System.Diagnostics.Process.GetProcessesByName("explorer").Length == 0)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = true });
    }

    private static class Native
    {
        private const uint WmSettingChange = 0x001A;
        private const uint SmtoAbortIfHung = 0x0002;
        private const uint SpiSetDeskWallpaper = 0x0014;
        private const uint SpifUpdateIniFileAndSendChange = 0x01 | 0x02;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
            uint flags, uint timeout, out UIntPtr result);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint action, uint param, string vparam, uint winIni);

        public static void BroadcastSettingChange(string area)
        {
            try { SendMessageTimeout(new IntPtr(0xffff), WmSettingChange, UIntPtr.Zero, area, SmtoAbortIfHung, 3000, out _); }
            catch { /* yoksay */ }
        }

        public static bool SetWallpaper(string path) =>
            SystemParametersInfo(SpiSetDeskWallpaper, 0, path, SpifUpdateIniFileAndSendChange);
    }
}

public static class EnvMerge
{
    /// <summary>
    /// PATH birleştirme: mevcut girdiler korunur, yedekteki eksik girdiler sona eklenir.
    /// Eski kullanıcı klasörü (C:\Users\eski) yeni kullanıcı klasörüne çevrilir. Tekrarlar eklenmez.
    /// </summary>
    public static string MergePath(string? current, string? backup, string? oldProfile, string? newProfile)
    {
        var result = Split(current).ToList();
        var seen = new HashSet<string>(result.Select(Norm), StringComparer.OrdinalIgnoreCase);
        foreach (var raw in Split(backup))
        {
            var entry = raw;
            if (!string.IsNullOrEmpty(oldProfile) && !string.IsNullOrEmpty(newProfile) &&
                entry.StartsWith(oldProfile.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                entry = newProfile.TrimEnd('\\') + entry[oldProfile.TrimEnd('\\').Length..];
            }
            if (seen.Add(Norm(entry))) result.Add(entry);
        }
        return string.Join(";", result);
    }

    private static IEnumerable<string> Split(string? s) =>
        (s ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Norm(string s) => s.Trim().TrimEnd('\\');
}
