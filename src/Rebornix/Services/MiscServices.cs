using System.Reflection;
using Rebornix.Helpers;
using Rebornix.Models;

namespace Rebornix.Services;

public static class RestorePointService
{
    /// <summary>Checkpoint-Computer ile Windows Sistem Geri Yükleme noktası oluşturur.</summary>
    public static async Task<(bool Ok, string Message)> CreateAsync(string description, bool dryRun, CancellationToken ct)
    {
        if (dryRun)
        {
            Log.Info(Loc.F("Dry_RestorePoint", description));
            return (true, "");
        }
        var script = $$"""
            $w = $null
            try {
              Checkpoint-Computer -Description {{SafePath.PsQuote(description)}} -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop -WarningVariable w -WarningAction SilentlyContinue
              if ($w) { 'WARN: ' + ($w -join ' ') } else { 'OK' }
            } catch { 'ERR: ' + $_.Exception.Message }
            """;
        var r = await ProcessRunner.PowerShellAsync(script, ct);
        var o = r.Output.Trim();
        if (o.StartsWith("OK")) return (true, Loc.Get("RestorePoint_Created"));
        if (o.StartsWith("WARN")) return (false, Loc.F("RestorePoint_Warn", o[5..].Trim()));
        return (false, Loc.F("RestorePoint_Failed", o.StartsWith("ERR") ? o[4..].Trim() : r.Error.Trim()));
    }
}

public sealed record BackupCandidate(string Root, BackupManifest? Manifest)
{
    public string Display => Manifest is null
        ? Root
        : $"{Root}  —  {Manifest.CreatedUtc.ToLocalTime():dd.MM.yyyy HH:mm}  ({Manifest.ComputerName})";
}

public static class BackupLocator
{
    /// <summary>Exe klasöründe ve tüm hazır sürücülerde (kök ve \Rebornix) yedek arar.</summary>
    public static List<BackupCandidate> Find()
    {
        var roots = new List<string> { AppPaths.BaseDir };
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                roots.Add(Path.Combine(d.RootDirectory.FullName, "Rebornix"));
                roots.Add(d.RootDirectory.FullName);
            }
            catch
            {
                // hazır olmayan sürücü
            }
        }

        var found = new List<BackupCandidate>();
        foreach (var r in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(r)) continue;
                var layout = new BackupLayout(r);
                if (!layout.LooksLikeBackup()) continue;
                BackupManifest? m = null;
                try { m = Json.Read<BackupManifest>(layout.ManifestFile); } catch { /* manifest bozuk */ }
                if (found.Any(f => string.Equals(f.Root, layout.Root, StringComparison.OrdinalIgnoreCase))) continue;
                found.Add(new BackupCandidate(layout.Root, m));
            }
            catch
            {
                // erişilemeyen klasör
            }
        }
        return found.OrderByDescending(f => f.Manifest?.CreatedUtc ?? DateTime.MinValue).ToList();
    }
}

public sealed class DriveOption
{
    public required string Root { get; init; }
    public required string Label { get; init; }
    public long FreeBytes { get; init; }
    public bool IsSystem { get; init; }
    public string Display => $"{Root}  {Label}  —  {Loc.F("Backup_FreeSpace", SafePath.FormatBytes(FreeBytes))}" +
                             (IsSystem ? "  ⚠ " + Loc.Get("Backup_SystemDriveTag") : "");

    public static List<DriveOption> List()
    {
        var sys = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var list = new List<DriveOption>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                list.Add(new DriveOption
                {
                    Root = d.RootDirectory.FullName,
                    Label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.DriveType.ToString() : d.VolumeLabel,
                    FreeBytes = d.AvailableFreeSpace,
                    IsSystem = string.Equals(d.RootDirectory.FullName, sys, StringComparison.OrdinalIgnoreCase)
                });
            }
            catch
            {
                // yoksay
            }
        }
        // Sistem dışı diskler önce
        return list.OrderBy(d => d.IsSystem).ThenBy(d => d.Root).ToList();
    }
}

public static class CatalogService
{
    public static string EmbeddedCatalogJson()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Rebornix.Data.katalog.json")
                      ?? throw new InvalidOperationException("Gömülü katalog bulunamadı.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>Data\katalog.json okunur; yoksa gömülü varsayılan katalog oraya yazılır.</summary>
    public static CatalogFile Load()
    {
        try
        {
            if (!File.Exists(AppPaths.CatalogFile))
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                File.WriteAllText(AppPaths.CatalogFile, EmbeddedCatalogJson());
            }
            var c = Json.Read<CatalogFile>(AppPaths.CatalogFile);
            if (c is not null && c.Apps.Count > 0) return Clean(c);
        }
        catch (Exception ex)
        {
            Log.Warn(Loc.F("Catalog_ReadFailed", ex.Message));
        }
        return Clean(System.Text.Json.JsonSerializer.Deserialize<CatalogFile>(EmbeddedCatalogJson(), Json.Options)!);
    }

    private static CatalogFile Clean(CatalogFile c)
    {
        var bad = c.Apps.Where(a => !WingetService.IsValidId(a.Id)).ToList();
        foreach (var b in bad) Log.Warn(Loc.F("Catalog_BadId", b.Name, b.Id));
        c.Apps = c.Apps.Except(bad).GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        foreach (var a in c.Apps.Where(a => !c.Categories.Contains(a.Category)))
            c.Categories.Add(a.Category);
        return c;
    }
}

public static class ProfileService
{
    public static Dictionary<string, List<string>> Load()
    {
        try
        {
            var p = Json.Read<Dictionary<string, List<string>>>(AppPaths.ProfilesFile);
            if (p is not null) return new Dictionary<string, List<string>>(p, StringComparer.CurrentCultureIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn(Loc.F("Profiles_ReadFailed", ex.Message));
        }

        // İlk çalıştırma: örnek profiller
        var defaults = new Dictionary<string, List<string>>(StringComparer.CurrentCultureIgnoreCase)
        {
            ["Minimal"] = ["Google.Chrome", "7zip.7zip", "VideoLAN.VLC", "Notepad++.Notepad++"],
            ["Oyun PC'si"] =
            [
                "Google.Chrome", "Valve.Steam", "EpicGames.EpicGamesLauncher", "GOG.Galaxy", "Discord.Discord",
                "Spotify.Spotify", "OBSProject.OBSStudio", "7zip.7zip", "CPUID.CPU-Z", "TechPowerUp.GPU-Z"
            ]
        };
        Save(defaults);
        return defaults;
    }

    public static void Save(Dictionary<string, List<string>> profiles) => Json.Write(AppPaths.ProfilesFile, profiles);
}
