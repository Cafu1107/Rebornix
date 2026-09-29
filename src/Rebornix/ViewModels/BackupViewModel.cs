using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rebornix.Helpers;
using Rebornix.Models;
using Rebornix.Services;
using Microsoft.Win32;

namespace Rebornix.ViewModels;

public sealed partial class BackupViewModel : ObservableObject, IPageActivated
{
    private readonly AppServices _app;
    private bool _drivesLoaded;

    public BackupViewModel(AppServices app, DriversViewModel drivers, WifiViewModel wifi, WindowsSettingsViewModel winSettings)
    {
        _app = app;
        DriversVm = drivers;
        WifiVm = wifi;
        WinSettingsVm = winSettings;
        foreach (var f in app.Settings.Current.CustomFolders)
            CustomFolders.Add(new CustomFolderItem(f));
    }

    public DriversViewModel DriversVm { get; }
    public WifiViewModel WifiVm { get; }
    public WindowsSettingsViewModel WinSettingsVm { get; }

    public ObservableCollection<DriveOption> Drives { get; } = [];
    public ObservableCollection<GameItem> Games { get; } = [];
    public ObservableCollection<CustomFolderItem> CustomFolders { get; } = [];
    public ObservableCollection<string> SummaryLines { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemDriveSelected))]
    private DriveOption? _selectedDrive;

    [ObservableProperty] private string _backupRoot = "";
    [ObservableProperty] private bool _includeDrivers = true;
    [ObservableProperty] private bool _includeApps = true;
    [ObservableProperty] private bool _includeSaves = true;
    [ObservableProperty] private bool _includeSettings = true;
    [ObservableProperty] private string _ludusaviStatus = "";
    [ObservableProperty] private bool _hasSummary;

    public bool IsSystemDriveSelected =>
        SelectedDrive?.IsSystem == true ||
        (BackupRoot.Length > 1 && string.Equals(Path.GetPathRoot(BackupRoot), Path.GetPathRoot(Environment.SystemDirectory),
            StringComparison.OrdinalIgnoreCase));

    public string GamesSelectionText => Loc.F("Saves_Selection", Games.Count(g => g.IsSelected), Games.Count,
        SafePath.FormatBytes(Games.Where(g => g.IsSelected).Sum(g => g.Save.Bytes)));

    public void OnActivated()
    {
        if (!_drivesLoaded) RefreshDrives();
        UpdateLudusaviStatus();
    }

    [RelayCommand]
    private void RefreshDrives()
    {
        _drivesLoaded = true;
        Drives.Clear();
        foreach (var d in DriveOption.List()) Drives.Add(d);

        // Varsayılan: exe sistem dışı bir diskteyse exe'nin klasörü, yoksa ilk sistem dışı disk
        var exeRoot = Path.GetPathRoot(AppPaths.BaseDir);
        var preferred = Drives.FirstOrDefault(d => !d.IsSystem && string.Equals(d.Root, exeRoot, StringComparison.OrdinalIgnoreCase))
                        ?? Drives.FirstOrDefault(d => !d.IsSystem)
                        ?? Drives.FirstOrDefault();
        SelectedDrive = preferred;
    }

    partial void OnSelectedDriveChanged(DriveOption? value)
    {
        if (value is null) return;
        var exeRoot = Path.GetPathRoot(AppPaths.BaseDir);
        BackupRoot = string.Equals(value.Root, exeRoot, StringComparison.OrdinalIgnoreCase) && !value.IsSystem
            ? AppPaths.BaseDir
            : Path.Combine(value.Root, "Rebornix");
    }

    partial void OnBackupRootChanged(string value) => OnPropertyChanged(nameof(IsSystemDriveSelected));

    [RelayCommand]
    private void BrowseRoot()
    {
        var dlg = new OpenFolderDialog { Title = Loc.Get("Backup_ChooseFolder") };
        if (dlg.ShowDialog() == true) BackupRoot = SafePath.Normalize(dlg.FolderName);
    }

    // ───────────── Save'ler ─────────────

    private void UpdateLudusaviStatus() =>
        LudusaviStatus = LudusaviService.IsInstalled ? Loc.Get("Saves_LudusaviReady") : Loc.Get("Saves_LudusaviMissing");

    [RelayCommand]
    private Task DownloadLudusaviAsync() => _app.Operation.RunAsync(Loc.Get("Ludusavi_DownloadTitle"), async ct =>
    {
        await _app.Ludusavi.DownloadLatestAsync(ct);
        UpdateLudusaviStatus();
    });

    [RelayCommand]
    private Task ScanGamesAsync() => _app.Operation.RunAsync(Loc.Get("Saves_Scanning"), ScanGamesCoreAsync);

    private async Task ScanGamesCoreAsync(CancellationToken ct)
    {
        if (!LudusaviService.IsInstalled)
        {
            if (!_app.Dialogs.Confirm(Loc.Get("Ludusavi_DownloadTitle"), Loc.Get("Ludusavi_DownloadQuestion"),
                    Loc.Get("Btn_Download"), Loc.Get("Btn_Cancel")))
                throw new OperationCanceledException();
            await _app.Ludusavi.DownloadLatestAsync(ct);
            UpdateLudusaviStatus();
        }
        _app.Operation.Report(null, Loc.Get("Saves_Scanning"));
        var games = await _app.Ludusavi.ScanAsync(ct);
        foreach (var g in Games) g.PropertyChanged -= OnGameChanged;
        Games.Clear();
        foreach (var g in games)
        {
            var item = new GameItem(g);
            item.PropertyChanged += OnGameChanged;
            Games.Add(item);
        }
        Log.Info(Loc.F("Saves_Found", Games.Count, SafePath.FormatBytes(games.Sum(g => g.Bytes))));
        OnPropertyChanged(nameof(GamesSelectionText));
    }

    private void OnGameChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameItem.IsSelected)) OnPropertyChanged(nameof(GamesSelectionText));
    }

    [RelayCommand]
    private void SelectAllGames(string? on)
    {
        var value = on != "0";
        foreach (var g in Games) g.IsSelected = value;
    }

    [RelayCommand]
    private void AddCustomFolder()
    {
        var dlg = new OpenFolderDialog { Title = Loc.Get("Custom_ChooseFolder"), Multiselect = true };
        if (dlg.ShowDialog() != true) return;
        foreach (var f in dlg.FolderNames) AddCustom(f);
    }

    [RelayCommand]
    private void AddSuggested(string? which)
    {
        var path = which switch
        {
            "mygames" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games"),
            "savedgames" => PathTokens.Expand("%SAVEDGAMES%"),
            _ => null
        };
        if (path is null) return;
        if (!Directory.Exists(path))
        {
            _app.Dialogs.Info(Loc.Get("Custom_Title"), Loc.F("Custom_SourceMissing", path));
            return;
        }
        AddCustom(path);
    }

    private void AddCustom(string path)
    {
        var sys = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var norm = SafePath.Normalize(path);
        if (string.Equals(norm.TrimEnd('\\') + "\\", sys, StringComparison.OrdinalIgnoreCase) ||
            SafePath.IsUnder(norm, Environment.GetFolderPath(Environment.SpecialFolder.Windows)))
        {
            _app.Dialogs.Warn(Loc.Get("Custom_Title"), Loc.Get("Custom_SystemFolderRejected"));
            return;
        }
        var token = PathTokens.Tokenize(norm);
        if (CustomFolders.Any(c => string.Equals(c.TokenPath, token, StringComparison.OrdinalIgnoreCase))) return;
        CustomFolders.Add(new CustomFolderItem(token));
        SaveCustomFolders();
        Log.Info(Loc.F("Custom_Added", token));
    }

    [RelayCommand]
    private void RemoveCustomFolder(CustomFolderItem? item)
    {
        if (item is null) return;
        CustomFolders.Remove(item);
        SaveCustomFolders();
    }

    private void SaveCustomFolders()
    {
        _app.Settings.Current.CustomFolders = CustomFolders.Select(c => c.TokenPath).ToList();
        _app.Settings.Save();
    }

    // ───────────── Yedekleme akışı ─────────────

    [RelayCommand]
    private async Task StartBackupAsync()
    {
        if (string.IsNullOrWhiteSpace(BackupRoot))
        {
            _app.Dialogs.Warn(Loc.Get("Backup_Title"), Loc.Get("Backup_NoTarget"));
            return;
        }
        if (!IncludeDrivers && !WifiVm.IsBackupEnabled && !IncludeApps && !IncludeSaves && !IncludeSettings)
        {
            _app.Dialogs.Warn(Loc.Get("Backup_Title"), Loc.Get("Backup_NothingSelected"));
            return;
        }

        string root;
        try { root = SafePath.Normalize(BackupRoot); }
        catch
        {
            _app.Dialogs.Warn(Loc.Get("Backup_Title"), Loc.Get("Backup_BadPath"));
            return;
        }

        if (IsSystemDriveSelected &&
            !_app.Dialogs.Confirm(Loc.Get("Backup_SystemDriveTitle"), Loc.Get("Backup_SystemDriveWarning"),
                Loc.Get("Backup_SystemDriveContinue"), Loc.Get("Btn_Cancel"), danger: true))
            return;

        // Wi-Fi: profiller yüklenmiş ve en az bir tane seçili olmalı; parola iki kez sorulur
        string? wifiPassword = null;
        if (WifiVm.IsBackupEnabled)
        {
            if (!WifiVm.IsLoaded) await WifiVm.LoadCommand.ExecuteAsync(null);
            if (WifiVm.SelectedCount == 0)
            {
                _app.Dialogs.Warn(Loc.Get("Wifi_Title"), Loc.Get("Wifi_NoneSelected"));
                return;
            }
            wifiPassword = _app.Dialogs.AskPassword(Loc.Get("Wifi_PasswordTitle"), Loc.Get("Wifi_PasswordMessage"), confirm: true);
            if (wifiPassword is null) return;
        }

        var layout = new BackupLayout(root);
        if (!_app.DryRun && Directory.Exists(root) && layout.LooksLikeBackup())
        {
            var choice = _app.Dialogs.Show(DialogKind.Question, Loc.Get("Backup_ExistingTitle"),
                Loc.F("Backup_ExistingMessage", root), Loc.Get("Backup_ExistingMove"), Loc.Get("Btn_Cancel"));
            if (choice != 0) return;
        }

        if (!_app.Dialogs.Confirm(Loc.Get("Backup_Title"), Loc.F("Backup_Confirm", root) + (_app.DryRun ? "\n\n" + Loc.Get("Dry_Banner") : "")))
            return;

        SummaryLines.Clear();
        HasSummary = false;
        try
        {
            await _app.Operation.RunAsync(Loc.Get("Backup_Title"), ct => RunBackupAsync(layout, wifiPassword, ct));
        }
        finally
        {
            wifiPassword = null;
        }
    }

    private async Task RunBackupAsync(BackupLayout layout, string? wifiPassword, CancellationToken ct)
    {
        var dry = _app.DryRun;
        var manifest = new BackupManifest
        {
            CreatedUtc = DateTime.UtcNow,
            ComputerName = Environment.MachineName,
            UserName = Environment.UserName,
            UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            WindowsVersion = Environment.OSVersion.VersionString,
            AppVersion = typeof(App).Assembly.GetName().Version?.ToString() ?? "",
            DryRun = dry
        };
        var lines = new List<string>();
        var steps = new List<(string Name, Func<Task> Run)>();

        if (!dry)
        {
            Directory.CreateDirectory(layout.Root);
            MovePreviousBackup(layout);
        }

        if (IncludeDrivers) steps.Add((Loc.Get("Step_Drivers"), async () =>
        {
            if (!DriversVm.IsLoaded) await DriversVm.LoadCoreAsync(ct);
            var selected = DriversVm.Items.Where(i => i.IsSelected).ToList();
            var r = await _app.Drivers.BackupAsync(selected, layout.DriversDir, dry, ct);
            manifest.DriverCount = r.Exported;
            manifest.Errors.AddRange(r.Problems);
            lines.Add(Loc.F("Sum_Drivers", r.Exported, SafePath.FormatBytes(r.TotalBytes)));
            if (r.Problems.Count > 0) lines.Add(Loc.F("Sum_DriverProblems", r.Problems.Count));
        }));

        if (WifiVm.IsBackupEnabled && wifiPassword is not null) steps.Add((Loc.Get("Step_Wifi"), async () =>
        {
            var n = await _app.Wifi.BackupAsync(WifiVm.SelectedNames(), wifiPassword, layout.WifiFile, dry, ct);
            manifest.WifiCount = n;
            lines.Add(Loc.F("Sum_Wifi", n));
        }));

        if (IncludeApps) steps.Add((Loc.Get("Step_Apps"), async () =>
        {
            var all = await Task.Run(InstalledAppsService.ReadAll, ct);
            if (!await _app.Winget.IsAvailableAsync(ct))
                throw new InvalidOperationException(Loc.Get("Winget_MissingForBackup"));
            if (dry)
            {
                Log.Info(Loc.F("Dry_WingetExport", layout.AppsFile));
                lines.Add(Loc.F("Sum_AppsDry", all.Count));
                return;
            }
            var r = await _app.Winget.ExportAsync(layout.AppsFile, ct);
            var manual = InstalledAppsService.BuildManualList(r.UnavailableNames, all, r.Packages.Select(p => p.Id));
            Json.Write(layout.ManualAppsFile, manual);
            Json.Write(layout.AllProgramsFile, all);
            manifest.AppCount = r.Packages.Count;
            manifest.ManualAppCount = manual.Count;
            lines.Add(Loc.F("Sum_Apps", r.Packages.Count, manual.Count));
        }));

        if (IncludeSaves) steps.Add((Loc.Get("Step_Saves"), async () =>
        {
            if (Games.Count == 0) await ScanGamesCoreAsync(ct);
            var selected = Games.Where(g => g.IsSelected).Select(g => g.Name).ToList();
            var r = await _app.Ludusavi.BackupAsync(selected, layout.LudusaviDir, dry, ct);
            manifest.GameCount = r.Games;
            manifest.Errors.AddRange(r.Errors);
            if (!dry && r.Games > 0)
            {
                Json.Write(layout.LudusaviInfoFile, new LudusaviBackupInfo
                {
                    UserProfile = manifest.UserProfile, CreatedUtc = DateTime.UtcNow, Games = selected
                });
            }
            lines.Add(Loc.F("Sum_Games", r.Games, SafePath.FormatBytes(r.Bytes)));

            var folders = CustomFolders.Where(c => c.IsSelected).ToList();
            if (folders.Count > 0)
            {
                var cm = await _app.CustomFolders.BackupAsync(folders.Select(f => f.TokenPath).ToList(), layout.CustomDir, dry,
                    (token, status) =>
                    {
                        var f = folders.FirstOrDefault(x => x.TokenPath == token);
                        if (f is not null) f.Status = status;
                    }, ct);
                manifest.CustomFolderCount = cm.Entries.Count;
                lines.Add(Loc.F("Sum_Custom", cm.Entries.Count));
            }
        }));

        if (IncludeSettings) steps.Add((Loc.Get("Step_Settings"), async () =>
        {
            var ids = WinSettingsVm.SelectedIds();
            if (ids.Count == 0) return;
            if (dry)
            {
                Log.Info(Loc.F("Dry_SettingsBackup", string.Join(", ", ids.Select(i => WindowsSettingsService.Def(i).Title))));
                manifest.SettingsItems = ids;
            }
            else
            {
                var backup = await Task.Run(() => _app.WindowsSettings.Capture(ids, layout.WindowsSettingsDir), ct);
                WindowsSettingsService.Save(backup, layout.WindowsSettingsFile);
                manifest.SettingsItems = backup.Items.Select(i => i.Id).ToList();
            }
            lines.Add(Loc.F("Sum_Settings", string.Join(", ", manifest.SettingsItems.Select(i => WindowsSettingsService.Def(i).Title))));
        }));

        for (var i = 0; i < steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (name, run) = steps[i];
            _app.Operation.Report(i, steps.Count, name);
            Log.Info("— " + name);
            try
            {
                await run();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var msg = $"{name}: {ErrorText.Friendly(ex)}";
                manifest.Errors.Add(msg);
                Log.Error(name, ex);
            }
        }
        _app.Operation.Report(100, Loc.Get("Backup_Finishing"));

        if (!dry)
        {
            CopyAppAndTools(layout);
            manifest.TotalBytes = DriverService.DirSize(layout.DriversDir) + DriverService.DirSize(layout.WifiDir) +
                                  DriverService.DirSize(layout.BackupsDir) + DriverService.DirSize(layout.WindowsSettingsDir);
            Json.Write(layout.ManifestFile, manifest);
            _app.Settings.Current.LastBackupRoot = layout.Root;
        }

        lines.Add(Loc.F("Sum_Total", SafePath.FormatBytes(manifest.TotalBytes)));
        lines.Add(manifest.Errors.Count == 0 ? Loc.Get("Sum_NoErrors") : Loc.F("Sum_Errors", manifest.Errors.Count));
        lines.AddRange(manifest.Errors.Select(e => "  • " + e));
        lines.Add(Loc.Get("Sum_BrowserNote"));
        if (dry) lines.Insert(0, Loc.Get("Dry_Banner"));

        SummaryLines.Clear();
        foreach (var l in lines) SummaryLines.Add(l);
        HasSummary = true;
        _app.Settings.SetSummary(Loc.F("Sum_BackupTitle", layout.Root), lines);
        foreach (var l in lines) Log.Info(l);

        _app.Dialogs.Show(manifest.Errors.Count == 0 ? DialogKind.Info : DialogKind.Warning,
            Loc.Get("Backup_DoneTitle"), string.Join("\n", lines), Loc.Get("Btn_Ok"));
    }

    /// <summary>Önceki yedek silinmez; _PreviousBackup_TARİH klasörüne taşınır.</summary>
    private static void MovePreviousBackup(BackupLayout layout)
    {
        if (!layout.LooksLikeBackup()) return;
        var dest = Path.Combine(layout.Root, "_PreviousBackup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(dest);
        foreach (var dir in new[] { layout.DriversDir, layout.WifiDir, layout.BackupsDir, layout.WindowsSettingsDir })
        {
            if (!Directory.Exists(dir)) continue;
            // Geri alma anlık görüntüleri bu bilgisayara ait, taşınmaz
            if (string.Equals(dir, layout.WindowsSettingsDir, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(layout.SnapshotsDir))
            {
                var wsDest = Path.Combine(dest, "WindowsSettings");
                Directory.CreateDirectory(wsDest);
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                {
                    if (string.Equals(entry, layout.SnapshotsDir, StringComparison.OrdinalIgnoreCase)) continue;
                    var target = Path.Combine(wsDest, Path.GetFileName(entry));
                    if (Directory.Exists(entry)) Directory.Move(entry, target);
                    else File.Move(entry, target);
                }
                continue;
            }
            Directory.Move(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }
        var dataDest = Path.Combine(dest, "Data");
        foreach (var f in new[] { layout.ManifestFile, layout.AppsFile, layout.ManualAppsFile, layout.AllProgramsFile, layout.ProgressFile })
        {
            if (!File.Exists(f)) continue;
            Directory.CreateDirectory(dataDest);
            File.Move(f, Path.Combine(dataDest, Path.GetFileName(f)));
        }
        Log.Info(Loc.F("Backup_PreviousMoved", dest));
    }

    /// <summary>Format sonrası kolaylık: exe, katalog, profiller ve Ludusavi yedek klasörüne kopyalanır.</summary>
    private static void CopyAppAndTools(BackupLayout layout)
    {
        if (string.Equals(layout.Root, AppPaths.BaseDir, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var exe = AppPaths.ExePath;
            if (exe is not null && File.Exists(exe) && Path.GetFileName(exe).Equals("Rebornix.exe", StringComparison.OrdinalIgnoreCase))
                File.Copy(exe, Path.Combine(layout.Root, "Rebornix.exe"), overwrite: true);

            Directory.CreateDirectory(layout.DataDir);
            foreach (var f in new[] { AppPaths.CatalogFile, AppPaths.ProfilesFile })
            {
                var target = Path.Combine(layout.DataDir, Path.GetFileName(f));
                if (File.Exists(f) && !File.Exists(target)) File.Copy(f, target);
            }
            if (LudusaviService.IsInstalled)
            {
                var toolsDest = Path.Combine(layout.Root, "Tools");
                Directory.CreateDirectory(toolsDest);
                var target = Path.Combine(toolsDest, "ludusavi.exe");
                if (!File.Exists(target)) File.Copy(LudusaviService.ExePath, target);
            }
            Log.Info(Loc.F("Backup_AppCopied", layout.Root));
        }
        catch (Exception ex)
        {
            Log.Warn(Loc.F("Backup_AppCopyFailed", ex.Message));
        }
    }
}
