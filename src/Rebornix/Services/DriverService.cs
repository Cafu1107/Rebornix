using System.Security.Principal;
using System.Text.Json;
using Rebornix.Helpers;
using Rebornix.Models;

namespace Rebornix.Services;

public static class Elevation
{
    public static bool IsAdmin
    {
        get
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}

public sealed record DriverBackupResult(int Exported, long TotalBytes, List<string> Problems);

public sealed record DriverInstallResult(ItemStatus Status, string Message, bool RebootRequired);

public sealed class DriverService
{
    private const int ErrorSuccessRebootRequired = 3010;
    private const int ErrorNoMoreItems = 259;

    private const string ListScript = """
        $ErrorActionPreference = 'Stop'
        $dev = @{}
        try {
          Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.InfName -like 'oem*.inf' } | ForEach-Object {
            $k = $_.InfName.ToLowerInvariant()
            if (-not $dev.ContainsKey($k)) { $dev[$k] = New-Object System.Collections.ArrayList }
            if ($_.DeviceName -and -not $dev[$k].Contains($_.DeviceName)) { [void]$dev[$k].Add($_.DeviceName) }
          }
        } catch { }
        $list = foreach ($d in Get-WindowsDriver -Online) {
          $folder = Split-Path $d.OriginalFileName -Parent
          $size = 0
          try { $size = (Get-ChildItem -LiteralPath $folder -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum } catch { }
          $devs = @()
          $key = $d.Driver.ToLowerInvariant()
          if ($dev.ContainsKey($key)) { $devs = @($dev[$key]) }
          [pscustomobject]@{
            PublishedName = $d.Driver
            OriginalFileName = $d.OriginalFileName
            InfName = (Split-Path $d.OriginalFileName -Leaf)
            FolderName = (Split-Path $folder -Leaf)
            ClassName = [string]$d.ClassName
            ClassDescription = [string]$d.ClassDescription
            Provider = [string]$d.ProviderName
            Version = [string]$d.Version
            Date = $(if ($d.Date) { $d.Date.ToString('yyyy-MM-dd') } else { '' })
            SizeBytes = [int64]$size
            Devices = $devs
          }
        }
        ConvertTo-Json -InputObject @($list) -Depth 4 -Compress
        """;

    /// <summary>Windows'a sonradan yüklenmiş (üçüncü parti) sürücü paketlerini listeler. Yönetici yetkisi gerekir.</summary>
    public async Task<List<DriverInfo>> ListAsync(CancellationToken ct)
    {
        var r = await ProcessRunner.PowerShellAsync(ListScript, ct);
        if (!r.Ok || string.IsNullOrWhiteSpace(r.Output))
        {
            var err = string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error;
            if (!Elevation.IsAdmin) throw new UnauthorizedAccessException(Loc.Get("Err_AdminRequired"));
            throw new InvalidOperationException(Loc.F("Drivers_ListFailed", FirstLine(err)));
        }
        var json = r.Output.Trim();
        return JsonSerializer.Deserialize<List<DriverInfo>>(json, Json.Options) ?? [];
    }

    /// <summary>
    /// Export-WindowsDriver ile tüm üçüncü parti sürücüleri geçici klasöre aktarır,
    /// seçilenleri kategori klasörlerine taşır, manifest yazar ve doğrular.
    /// </summary>
    public async Task<DriverBackupResult> BackupAsync(IReadOnlyList<DriverItem> selected, string driversDir,
        bool dryRun, CancellationToken ct)
    {
        var problems = new List<string>();
        if (selected.Count == 0) return new DriverBackupResult(0, 0, problems);

        if (dryRun)
        {
            Log.Info(Loc.F("Dry_DriverExport", driversDir));
            foreach (var d in selected)
                Log.Info(Loc.F("Dry_DriverItem", d.CategoryName, d.DisplayName, d.Info.Version));
            return new DriverBackupResult(selected.Count, selected.Sum(d => d.Info.SizeBytes), problems);
        }

        Directory.CreateDirectory(driversDir);
        var staging = Path.Combine(driversDir, "_disa_aktarim_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(staging);
        try
        {
            Log.Info(Loc.Get("Drivers_Exporting"));
            var r = await ProcessRunner.PowerShellAsync(
                $"$ErrorActionPreference='Stop'; Export-WindowsDriver -Online -Destination {SafePath.PsQuote(staging)} | Out-Null; 'OK'",
                ct, logScript: true);
            if (!r.Ok || !r.Output.Contains("OK"))
                throw new InvalidOperationException(Loc.F("Drivers_ExportFailed", FirstLine(r.Error + r.Output)));

            var entries = new List<DriverManifestEntry>();
            long total = 0;
            foreach (var d in selected)
            {
                ct.ThrowIfCancellationRequested();
                var folderName = SafePath.SanitizeFileName(d.Info.FolderName, 200);
                var src = Path.Combine(staging, folderName);
                if (!Directory.Exists(src))
                {
                    problems.Add(Loc.F("Drivers_NotExported", d.DisplayName));
                    continue;
                }
                var rel = Path.Combine(DriverCategories.FolderName(d.Category), folderName);
                var dest = SafePath.Combine(driversDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (Directory.Exists(dest)) dest = dest + "_" + DateTime.Now.Ticks;
                Directory.Move(src, dest);

                var size = DirSize(dest);
                total += size;
                entries.Add(new DriverManifestEntry
                {
                    DisplayName = d.DisplayName,
                    FolderName = folderName,
                    RelativePath = Path.GetRelativePath(driversDir, dest),
                    InfName = d.Info.InfName,
                    Provider = d.Info.Provider,
                    Version = d.Info.Version,
                    Date = d.Info.Date,
                    ClassName = d.Info.ClassName,
                    Category = d.Category,
                    SizeBytes = size
                });
            }

            Json.Write(Path.Combine(driversDir, "drivers.json"), entries);
            problems.AddRange(Verify(driversDir, entries));
            Log.Success(Loc.F("Drivers_BackupDone", entries.Count, SafePath.FormatBytes(total)));
            return new DriverBackupResult(entries.Count, total, problems);
        }
        finally
        {
            // Seçilmeyen sürücülerin kaldığı geçici dışa aktarma klasörü (uygulamanın kendi ara çıktısı)
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (Exception ex) { Log.Warn(Loc.F("Drivers_StagingCleanupFailed", staging, ex.Message)); }
        }
    }

    /// <summary>Her paket klasöründe INF var mı ve boyut sıfırdan büyük mü?</summary>
    public static List<string> Verify(string driversDir, IEnumerable<DriverManifestEntry> entries)
    {
        var problems = new List<string>();
        foreach (var e in entries)
        {
            var dir = SafePath.Combine(driversDir, e.RelativePath);
            if (!Directory.Exists(dir)) { problems.Add(Loc.F("Drivers_VerifyMissing", e.DisplayName)); continue; }
            if (!Directory.EnumerateFiles(dir, "*.inf", SearchOption.AllDirectories).Any())
                problems.Add(Loc.F("Drivers_VerifyNoInf", e.DisplayName));
            if (DirSize(dir) == 0) problems.Add(Loc.F("Drivers_VerifyEmpty", e.DisplayName));
        }
        return problems;
    }

    public static List<DriverManifestEntry> LoadManifest(string driversDir)
    {
        var file = Path.Combine(driversDir, "drivers.json");
        var list = Json.Read<List<DriverManifestEntry>>(file);
        if (list is not null) return list;

        // Manifest yoksa (ör. elle kopyalanmış klasör) INF içeren klasörleri bul
        if (!Directory.Exists(driversDir)) return [];
        return Directory.EnumerateFiles(driversDir, "*.inf", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(d => new DriverManifestEntry
            {
                DisplayName = Path.GetFileName(d!),
                FolderName = Path.GetFileName(d!),
                RelativePath = Path.GetRelativePath(driversDir, d!),
                InfName = Directory.EnumerateFiles(d!, "*.inf").Select(Path.GetFileName).FirstOrDefault() ?? "",
                Category = DriverCategory.Other
            }).ToList();
    }

    /// <summary>Ağ sürücüleri önce, sonra kategori sırası.</summary>
    public static List<DriverManifestEntry> OrderForInstall(IEnumerable<DriverManifestEntry> entries) =>
        entries.OrderBy(e => e.Category == DriverCategory.Network ? 0 : 1)
            .ThenBy(e => (int)e.Category)
            .ThenBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Sistemde aynı INF'in eşit veya daha yeni sürümü varsa true (üzerine eski sürüm yazılmaz).</summary>
    public static bool HasSameOrNewer(DriverManifestEntry e, IEnumerable<DriverInfo> installed, out string installedVersion)
    {
        installedVersion = "";
        if (!Version.TryParse(e.Version, out var backupVer)) return false;
        foreach (var d in installed)
        {
            if (!string.Equals(d.InfName, e.InfName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(e.Provider) &&
                !string.Equals(d.Provider, e.Provider, StringComparison.OrdinalIgnoreCase)) continue;
            if (Version.TryParse(d.Version, out var v) && v >= backupVer)
            {
                installedVersion = d.Version;
                return true;
            }
        }
        return false;
    }

    public async Task<DriverInstallResult> InstallAsync(DriverManifestEntry e, string driversDir, bool dryRun,
        CancellationToken ct)
    {
        var dir = SafePath.Combine(driversDir, e.RelativePath);
        var args = $"/add-driver {SafePath.Quote(Path.Combine(dir, "*.inf"))} /subdirs /install";
        if (dryRun)
        {
            Log.Info(Loc.F("Dry_DriverInstall", e.DisplayName, "pnputil " + args));
            return new DriverInstallResult(ItemStatus.DryRun, "", false);
        }
        if (!Directory.Exists(dir))
            return new DriverInstallResult(ItemStatus.Failed, Loc.Get("Drivers_FolderMissing"), false);

        var r = await ProcessRunner.RunAsync(ProcessRunner.SystemExe("pnputil.exe"), args, ct);
        return r.ExitCode switch
        {
            0 => new DriverInstallResult(ItemStatus.Success, "", false),
            ErrorSuccessRebootRequired => new DriverInstallResult(ItemStatus.Success, Loc.Get("Drivers_RebootNeeded"), true),
            ErrorNoMoreItems => new DriverInstallResult(ItemStatus.Success, Loc.Get("Drivers_AddedNoDevice"), false),
            _ => new DriverInstallResult(ItemStatus.Failed, Loc.F("Common_ExitCode", r.ExitCode) + " " + FirstLine(r.Output), false)
        };
    }

    public static long DirSize(string dir)
    {
        try
        {
            return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }

    private static string FirstLine(string s) =>
        s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
}
