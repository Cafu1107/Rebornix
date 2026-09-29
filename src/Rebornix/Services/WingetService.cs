using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rebornix.Helpers;
using Rebornix.Models;

namespace Rebornix.Services;

public sealed record WingetExportResult(List<WingetPackageRef> Packages, List<string> UnavailableNames);

public sealed record InstallResult(ItemStatus Status, string Message, int ExitCode);

public sealed partial class WingetService
{
    // winget çıkış kodları (APPINSTALLER_CLI_ERROR_*)
    private const int NoApplicationsFound = unchecked((int)0x8A150014);
    private const int UpdateNotApplicable = unchecked((int)0x8A15002B);
    private const int PackageAlreadyInstalled = unchecked((int)0x8A150061);
    private const int InstallRebootRequiredToFinish = unchecked((int)0x8A150109);
    private const int InstallRebootRequiredForInstall = unchecked((int)0x8A15010A);
    private const int InstallCancelledByUser = unchecked((int)0x8A150104);
    private const int InstallContactSupport = unchecked((int)0x8A150101);
    private const int DownloadFailed = unchecked((int)0x8A150008);
    private const int NoNetwork = unchecked((int)0x8A150007);

    private string? _exe;

    /// <summary>winget.exe yolunu bulur (yönetici oturumunda PATH'te olmayabilir).</summary>
    private string? FindExe()
    {
        if (_exe is not null && File.Exists(_exe)) return _exe;
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WindowsApps\winget.exe")
        };
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try { candidates.Add(Path.Combine(dir.Trim(), "winget.exe")); } catch { /* geçersiz PATH girdisi */ }
        }
        _exe = candidates.FirstOrDefault(File.Exists);
        return _exe;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var exe = FindExe();
        if (exe is null) return false;
        try
        {
            var r = await ProcessRunner.RunAsync(exe, "--version", ct, Encoding.UTF8);
            return r.Ok && r.Output.Trim().StartsWith('v');
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// winget yoksa: 1) Önceden yüklü App Installer paketini kaydetmeyi dener (temiz kurulumda sık görülen durum),
    /// 2) olmazsa resmi Microsoft adresinden App Installer'ı ve bağımlılıklarını indirip kurar.
    /// </summary>
    public async Task<bool> TryInstallAsync(bool dryRun, CancellationToken ct)
    {
        if (dryRun)
        {
            Log.Info(Loc.Get("Dry_WingetInstall"));
            return false;
        }

        Log.Info(Loc.Get("Winget_TryRegister"));
        await ProcessRunner.PowerShellAsync(
            "try { Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe -ErrorAction Stop; 'OK' } catch { 'ERR: ' + $_.Exception.Message }",
            ct);
        _exe = null;
        if (await IsAvailableAsync(ct)) return true;

        if (!await NetworkService.HasInternetAsync(ct))
        {
            Log.Warn(Loc.Get("Winget_NoInternetForInstall"));
            return false;
        }

        Log.Info(Loc.Get("Winget_Downloading"));
        var tmp = Path.Combine(Path.GetTempPath(), "Rebornix_winget_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var script = $$"""
                $ErrorActionPreference = 'Stop'
                $d = {{SafePath.PsQuote(tmp)}}
                $deps = @()
                try {
                  Invoke-WebRequest -UseBasicParsing 'https://aka.ms/Microsoft.VCLibs.x64.14.00.Desktop.appx' -OutFile "$d\vclibs.appx"
                  $deps += "$d\vclibs.appx"
                } catch { }
                try {
                  Invoke-WebRequest -UseBasicParsing 'https://github.com/microsoft/microsoft-ui-xaml/releases/download/v2.8.6/Microsoft.UI.Xaml.2.8.x64.appx' -OutFile "$d\uixaml.appx"
                  $deps += "$d\uixaml.appx"
                } catch { }
                Invoke-WebRequest -UseBasicParsing 'https://aka.ms/getwinget' -OutFile "$d\appinstaller.msixbundle"
                foreach ($x in $deps) { try { Add-AppxPackage -Path $x -ErrorAction Stop } catch { } }
                Add-AppxPackage -Path "$d\appinstaller.msixbundle"
                'OK'
                """;
            var r = await ProcessRunner.PowerShellAsync(script, ct);
            if (!r.Output.Contains("OK")) Log.Warn(Loc.F("Winget_InstallFailedDetail", FirstLine(r.Error)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn(Loc.F("Winget_InstallFailedDetail", ex.Message));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { /* geçici indirme klasörü */ }
        }

        _exe = null;
        return await IsAvailableAsync(ct);
    }

    private string Exe => FindExe() ?? throw new InvalidOperationException(Loc.Get("Winget_Missing"));

    /// <summary>winget export: tanınan paket kimlikleri + winget'in tanımadığı program adları.</summary>
    public async Task<WingetExportResult> ExportAsync(string jsonFile, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(jsonFile)!);
        var r = await ProcessRunner.RunAsync(Exe,
            $"export -o {SafePath.Quote(jsonFile)} --accept-source-agreements --disable-interactivity",
            ct, Encoding.UTF8);
        if (!File.Exists(jsonFile))
            throw new InvalidOperationException(Loc.F("Winget_ExportFailed", r.ExitCode));

        var packages = ParseExportFile(jsonFile);
        var unavailable = ParseUnavailable(r.Output + "\n" + r.Error);
        return new WingetExportResult(packages, unavailable);
    }

    public static List<WingetPackageRef> ParseExportFile(string jsonFile)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonFile));
        var list = new List<WingetPackageRef>();
        if (!doc.RootElement.TryGetProperty("Sources", out var sources)) return list;
        foreach (var s in sources.EnumerateArray())
        {
            var sourceName = s.TryGetProperty("SourceDetails", out var det) && det.TryGetProperty("Name", out var n)
                ? n.GetString() ?? "winget"
                : "winget";
            if (!s.TryGetProperty("Packages", out var pkgs)) continue;
            foreach (var p in pkgs.EnumerateArray())
            {
                if (p.TryGetProperty("PackageIdentifier", out var id) && !string.IsNullOrWhiteSpace(id.GetString()))
                    list.Add(new WingetPackageRef { Id = id.GetString()!, Source = sourceName });
            }
        }
        return list;
    }

    /// <summary>"Installed package is not available from any source: X" satırlarından program adlarını çıkarır.</summary>
    internal static List<string> ParseUnavailable(string output) =>
        output.Split('\n')
            .Select(l => UnavailableRegex().Match(l.Trim()))
            .Where(m => m.Success)
            .Select(m => m.Groups["name"].Value.Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    [GeneratedRegex(@"^Installed package is not available from any source:\s*(?<name>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex UnavailableRegex();

    /// <summary>Kurulu ve winget'in tanıdığı paket kimlikleri (katalogda "Kurulu" rozeti için).</summary>
    public async Task<HashSet<string>> GetInstalledIdsAsync(CancellationToken ct)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"Rebornix_kurulu_{Guid.NewGuid():N}.json");
        try
        {
            await ProcessRunner.RunAsync(Exe,
                $"export -o {SafePath.Quote(tmp)} --accept-source-agreements --disable-interactivity", ct, Encoding.UTF8);
            if (!File.Exists(tmp)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return new HashSet<string>(ParseExportFile(tmp).Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* geçici dosya */ }
        }
    }

    public static bool IsValidId(string id) => IdRegex().IsMatch(id);

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.\-_+]{0,127}$")]
    private static partial Regex IdRegex();

    public static string BuildInstallArgs(string id, string? source)
    {
        var src = string.IsNullOrWhiteSpace(source) ? "" : $" --source {source}";
        return $"install --id {id} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity{src}";
    }

    /// <summary>Tek bir paketi sessiz kurar.</summary>
    public async Task<InstallResult> InstallAsync(string id, string? source, bool dryRun, CancellationToken ct)
    {
        if (!IsValidId(id)) return new InstallResult(ItemStatus.Failed, Loc.Get("Winget_InvalidId"), -1);
        if (source is not null && !IsValidId(source)) source = null;
        var args = BuildInstallArgs(id, source);
        if (dryRun)
        {
            Log.Info(Loc.F("Dry_WingetInstallApp", "winget " + args));
            return new InstallResult(ItemStatus.DryRun, "", 0);
        }

        var r = await ProcessRunner.RunAsync(Exe, args, ct, Encoding.UTF8);
        return r.ExitCode switch
        {
            0 => new InstallResult(ItemStatus.Success, "", 0),
            PackageAlreadyInstalled or UpdateNotApplicable =>
                new InstallResult(ItemStatus.AlreadyInstalled, Loc.Get("Winget_AlreadyInstalled"), r.ExitCode),
            InstallRebootRequiredToFinish or InstallRebootRequiredForInstall =>
                new InstallResult(ItemStatus.Success, Loc.Get("Winget_RebootRequired"), r.ExitCode),
            _ => new InstallResult(ItemStatus.Failed, DescribeError(r.ExitCode), r.ExitCode)
        };
    }

    public static string DescribeError(int code) => code switch
    {
        NoApplicationsFound => Loc.Get("Winget_ErrNotFound"),
        InstallCancelledByUser => Loc.Get("Winget_ErrCancelled"),
        InstallContactSupport => Loc.Get("Winget_ErrInstaller"),
        DownloadFailed or NoNetwork => Loc.Get("Winget_ErrDownload"),
        _ => Loc.F("Common_ExitCode", $"0x{code:X8}")
    };

    private static string FirstLine(string s) =>
        s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
}
