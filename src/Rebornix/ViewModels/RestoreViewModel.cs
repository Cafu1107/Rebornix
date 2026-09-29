using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rebornix.Helpers;
using Rebornix.Models;
using Rebornix.Services;
using Microsoft.Win32;

namespace Rebornix.ViewModels;

public sealed record PolicyOption(ConflictPolicy Value, string Title);

public sealed partial class RestoreViewModel : ObservableObject, IPageActivated
{
    private const int MaxPasswordAttempts = 5;
    private readonly AppServices _app;
    private BackupLayout? _layout;
    private BackupManifest? _manifest;
    private RestoreProgress _progress = new();
    private WifiPayload? _wifiPayload;
    private bool _searched;

    public RestoreViewModel(AppServices app)
    {
        _app = app;
        Policies =
        [
            new(ConflictPolicy.KeepNewer, Loc.Get("Policy_KeepNewer")),
            new(ConflictPolicy.UseBackup, Loc.Get("Policy_UseBackup")),
            new(ConflictPolicy.KeepExisting, Loc.Get("Policy_KeepExisting"))
        ];
        _selectedPolicy = Policies[0];
    }

    public ObservableCollection<BackupCandidate> Candidates { get; } = [];
    public ObservableCollection<StatusItem> DriverItems { get; } = [];
    public ObservableCollection<StatusItem> WifiItems { get; } = [];
    public ObservableCollection<StatusItem> AppItems { get; } = [];
    public ObservableCollection<ManualApp> ManualApps { get; } = [];
    public ObservableCollection<GameItem> GameItems { get; } = [];
    public ObservableCollection<StatusItem> CustomItems { get; } = [];
    public ObservableCollection<StatusItem> SettingItems { get; } = [];
    public ObservableCollection<string> FailedLines { get; } = [];
    public IReadOnlyList<PolicyOption> Policies { get; }

    [ObservableProperty] private BackupCandidate? _selectedCandidate;
    [ObservableProperty] private string _backupInfo = "";
    [ObservableProperty] private bool _hasBackup;

    [ObservableProperty] private bool _doDrivers = true;
    [ObservableProperty] private bool _doWifi = true;
    [ObservableProperty] private bool _doApps = true;
    [ObservableProperty] private bool _doSaves = true;
    [ObservableProperty] private bool _doSettings = true;

    [ObservableProperty] private bool _hasDrivers;
    [ObservableProperty] private bool _hasWifi;
    [ObservableProperty] private bool _hasApps;
    [ObservableProperty] private bool _hasSaves;
    [ObservableProperty] private bool _hasSettings;

    [ObservableProperty] private ItemStatus _driversState;
    [ObservableProperty] private ItemStatus _wifiState;
    [ObservableProperty] private ItemStatus _appsState;
    [ObservableProperty] private ItemStatus _savesState;
    [ObservableProperty] private ItemStatus _settingsState;

    [ObservableProperty] private string _resumeInfo = "";
    [ObservableProperty] private bool _rebootRequired;
    [ObservableProperty] private bool _wifiUnlocked;
    [ObservableProperty] private string _internetStatus = "";
    [ObservableProperty] private bool _internetOk;
    [ObservableProperty] private string? _connectProfile;
    [ObservableProperty] private PolicyOption _selectedPolicy;
    [ObservableProperty] private bool _hasFailures;

    public ObservableCollection<string> ConnectableProfiles { get; } = [];

    public void OnActivated()
    {
        if (_searched) return;
        _searched = true;
        FindBackups();
    }

    // ───────────── Yedek bulma / yükleme ─────────────

    [RelayCommand]
    private void FindBackups()
    {
        Candidates.Clear();
        foreach (var c in BackupLocator.Find()) Candidates.Add(c);
        if (Candidates.Count > 0) SelectedCandidate = Candidates[0];
        else
        {
            HasBackup = false;
            BackupInfo = Loc.Get("Restore_NoneFound");
        }
    }

    [RelayCommand]
    private void BrowseBackup()
    {
        var dlg = new OpenFolderDialog { Title = Loc.Get("Restore_ChooseFolder") };
        if (dlg.ShowDialog() != true) return;
        var layout = new BackupLayout(dlg.FolderName);
        if (!layout.LooksLikeBackup())
        {
            _app.Dialogs.Warn(Loc.Get("Restore_Title"), Loc.Get("Restore_NotABackup"));
            return;
        }
        BackupManifest? m = null;
        try { m = Json.Read<BackupManifest>(layout.ManifestFile); } catch { /* manifest yok */ }
        var c = new BackupCandidate(layout.Root, m);
        Candidates.Insert(0, c);
        SelectedCandidate = c;
    }

    partial void OnSelectedCandidateChanged(BackupCandidate? value)
    {
        if (value is null) return;
        try
        {
            LoadBackup(value);
        }
        catch (Exception ex)
        {
            Log.Error(Loc.Get("Restore_LoadFailed"), ex);
            _app.Dialogs.Error(Loc.Get("Restore_Title"), ErrorText.Friendly(ex));
        }
    }

    private void LoadBackup(BackupCandidate c)
    {
        _layout = new BackupLayout(c.Root);
        _manifest = c.Manifest;
        _wifiPayload = null;
        WifiUnlocked = false;
        HasBackup = true;

        BackupInfo = _manifest is null
            ? Loc.F("Restore_InfoNoManifest", _layout.Root)
            : Loc.F("Restore_Info", _layout.Root, _manifest.CreatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                _manifest.ComputerName, _manifest.UserName);

        _progress = Json.Read<RestoreProgress>(_layout.ProgressFile) ?? new RestoreProgress();
        RebootRequired = _progress.RebootRequired;
        ResumeInfo = _progress.UpdatedUtc == default
            ? ""
            : Loc.F("Restore_ResumeInfo", _progress.UpdatedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));

        // Sürücüler
        DriverItems.Clear();
        foreach (var e in DriverService.OrderForInstall(DriverService.LoadManifest(_layout.DriversDir)))
        {
            var done = _progress.Drivers.TryGetValue(e.RelativePath, out var st) && st == ItemStatus.Success;
            DriverItems.Add(new StatusItem
            {
                Key = e.RelativePath, Name = e.DisplayName, Tag = e,
                Detail = $"{DriverCategories.DisplayName(e.Category)} · {e.Provider} · {e.Version}",
                Status = done ? ItemStatus.Success : ItemStatus.Pending,
                Message = done ? Loc.Get("Restore_DoneBefore") : "",
                IsSelected = !done
            });
        }
        HasDrivers = DriverItems.Count > 0;
        DoDrivers = HasDrivers && !_progress.DriversDone;
        DriversState = _progress.DriversDone ? ItemStatus.Success : ItemStatus.Pending;

        // Wi-Fi
        WifiItems.Clear();
        HasWifi = File.Exists(_layout.WifiFile);
        DoWifi = HasWifi && !_progress.WifiDone;
        WifiState = _progress.WifiDone ? ItemStatus.Success : ItemStatus.Pending;

        // Uygulamalar
        AppItems.Clear();
        if (File.Exists(_layout.AppsFile))
        {
            foreach (var p in WingetService.ParseExportFile(_layout.AppsFile)
                         .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            {
                var done = _progress.Apps.TryGetValue(p.Id, out var st) && st is ItemStatus.Success or ItemStatus.AlreadyInstalled;
                AppItems.Add(new StatusItem
                {
                    Key = p.Id, Name = p.Id, Detail = p.Source, Tag = p,
                    Status = done ? st : ItemStatus.Pending,
                    IsSelected = !done
                });
            }
        }
        ManualApps.Clear();
        foreach (var m in Json.Read<List<ManualApp>>(_layout.ManualAppsFile) ?? []) ManualApps.Add(m);
        HasApps = AppItems.Count > 0;
        DoApps = HasApps && !_progress.AppsDone;
        AppsState = _progress.AppsDone ? ItemStatus.Success : ItemStatus.Pending;

        // Save'ler
        GameItems.Clear();
        CustomItems.Clear();
        var custom = Json.Read<CustomFolderManifest>(_layout.CustomManifest);
        foreach (var e in custom?.Entries ?? [])
            CustomItems.Add(new StatusItem { Key = e.TokenPath, Name = e.TokenPath, Tag = e, Detail = SafePath.FormatBytes(e.Bytes) });
        HasSaves = Directory.Exists(_layout.LudusaviDir) || CustomItems.Count > 0;
        DoSaves = HasSaves && !_progress.SavesDone;
        SavesState = _progress.SavesDone ? ItemStatus.Success : ItemStatus.Pending;

        // Windows ayarları
        SettingItems.Clear();
        var ws = File.Exists(_layout.WindowsSettingsFile) ? WindowsSettingsService.Load(_layout.WindowsSettingsFile) : null;
        foreach (var item in ws?.Items ?? [])
        {
            var def = WindowsSettingsService.Definitions.FirstOrDefault(d => d.Id == item.Id);
            if (def is null) continue;
            SettingItems.Add(new StatusItem { Key = def.Id, Name = def.Title, Detail = def.Description });
        }
        HasSettings = SettingItems.Count > 0;
        DoSettings = HasSettings && !_progress.SettingsDone;
        SettingsState = _progress.SettingsDone ? ItemStatus.Success : ItemStatus.Pending;

        FailedLines.Clear();
        HasFailures = false;
        Log.Info(Loc.F("Restore_Loaded", _layout.Root));
    }

    private void SaveProgress()
    {
        if (_layout is null || _app.DryRun) return;
        try
        {
            _progress.UpdatedUtc = DateTime.UtcNow;
            Json.Write(_layout.ProgressFile, _progress);
        }
        catch (Exception ex)
        {
            Log.Warn(Loc.F("Restore_ProgressSaveFailed", ex.Message));
        }
    }

    private BackupLayout Layout => _layout ?? throw new InvalidOperationException(Loc.Get("Restore_NoBackupSelected"));

    // ───────────── Tümünü çalıştır ─────────────

    [RelayCommand]
    private Task RunAllAsync()
    {
        if (_layout is null)
        {
            _app.Dialogs.Warn(Loc.Get("Restore_Title"), Loc.Get("Restore_NoBackupSelected"));
            return Task.CompletedTask;
        }
        var steps = new List<string>();
        if (DoDrivers && HasDrivers) steps.Add("1. " + Loc.Get("Step_Drivers"));
        if (DoWifi && HasWifi) steps.Add("2. " + Loc.Get("Step_Wifi"));
        if (DoApps && HasApps) steps.Add("3. " + Loc.Get("Step_Apps"));
        if (DoSaves && HasSaves) steps.Add("4. " + Loc.Get("Step_Saves"));
        if (DoSettings && HasSettings) steps.Add("5. " + Loc.Get("Step_Settings"));
        if (steps.Count == 0)
        {
            _app.Dialogs.Info(Loc.Get("Restore_Title"), Loc.Get("Restore_NothingToDo"));
            return Task.CompletedTask;
        }
        if (!_app.Dialogs.Confirm(Loc.Get("Restore_Title"),
                Loc.F("Restore_RunAllConfirm", string.Join("\n", steps)) + (_app.DryRun ? "\n\n" + Loc.Get("Dry_Banner") : ""),
                danger: !_app.DryRun))
            return Task.CompletedTask;

        return _app.Operation.RunAsync(Loc.Get("Restore_Title"), async ct =>
        {
            FailedLines.Clear();
            if (DoDrivers && HasDrivers)
            {
                await DriversCoreAsync(ct, askConfirm: false);
                if (RebootRequired &&
                    _app.Dialogs.Confirm(Loc.Get("Restore_RebootTitle"), Loc.Get("Restore_RebootStop"),
                        Loc.Get("Restore_RebootStopYes"), Loc.Get("Restore_RebootContinue")))
                {
                    Log.Warn(Loc.Get("Restore_StoppedForReboot"));
                    return;
                }
            }
            if (DoWifi && HasWifi) await WifiCoreAsync(ct, askConfirm: false);
            if (DoApps && HasApps) await AppsCoreAsync(ct, askConfirm: false, onlyFailed: false);
            if (DoSaves && HasSaves) await SavesCoreAsync(ct, askConfirm: false);
            if (DoSettings && HasSettings) await SettingsCoreAsync(ct, askConfirm: false);
            FinishRun();
        });
    }

    private void FinishRun()
    {
        FailedLines.Clear();
        foreach (var d in DriverItems.Where(i => i.Status == ItemStatus.Failed))
            FailedLines.Add($"{Loc.Get("Step_Drivers")}: {d.Name} — {d.Message}");
        foreach (var w in WifiItems.Where(i => i.Status == ItemStatus.Failed))
            FailedLines.Add($"{Loc.Get("Step_Wifi")}: {w.Name}");
        foreach (var a in AppItems.Where(i => i.Status == ItemStatus.Failed))
            FailedLines.Add($"{Loc.Get("Step_Apps")}: {a.Name} — {a.Message}");
        foreach (var g in GameItems.Where(i => i.Status == ItemStatus.Failed))
            FailedLines.Add($"{Loc.Get("Step_Saves")}: {g.Name} — {g.Message}");
        foreach (var s in SettingItems.Where(i => i.Status == ItemStatus.Failed))
            FailedLines.Add($"{Loc.Get("Step_Settings")}: {s.Name} — {s.Message}");
        HasFailures = FailedLines.Count > 0;

        var lines = new List<string>
        {
            Loc.F("Sum_RestoreDrivers", DriverItems.Count(i => i.Status is ItemStatus.Success or ItemStatus.DryRun), DriverItems.Count),
            Loc.F("Sum_RestoreWifi", WifiItems.Count(i => i.Status is ItemStatus.Success or ItemStatus.DryRun)),
            Loc.F("Sum_RestoreApps", AppItems.Count(i => i.Status is ItemStatus.Success or ItemStatus.AlreadyInstalled or ItemStatus.DryRun), AppItems.Count),
            Loc.F("Sum_RestoreGames", GameItems.Count(i => i.Status is ItemStatus.Success or ItemStatus.DryRun)),
            Loc.F("Sum_RestoreSettings", SettingItems.Count(i => i.Status is ItemStatus.Success or ItemStatus.DryRun)),
            HasFailures ? Loc.F("Sum_Errors", FailedLines.Count) : Loc.Get("Sum_NoErrors")
        };
        if (ManualApps.Count > 0) lines.Add(Loc.F("Sum_ManualReminder", ManualApps.Count));
        if (RebootRequired) lines.Add(Loc.Get("Restore_RebootNote"));
        if (_app.DryRun) lines.Insert(0, Loc.Get("Dry_Banner"));
        _app.Settings.SetSummary(Loc.F("Sum_RestoreTitle", Layout.Root), lines);
        _app.Dialogs.Show(HasFailures ? DialogKind.Warning : DialogKind.Info, Loc.Get("Restore_DoneTitle"),
            string.Join("\n", lines), Loc.Get("Btn_Ok"));
    }

    // ───────────── ADIM 1: Sürücüler ─────────────

    [RelayCommand]
    private Task RunDriversAsync() =>
        _app.Operation.RunAsync(Loc.Get("Step_Drivers"), ct => DriversCoreAsync(ct, askConfirm: true));

    private async Task DriversCoreAsync(CancellationToken ct, bool askConfirm)
    {
        var items = DriverItems.Where(i => i.IsSelected && i.Status != ItemStatus.Success).ToList();
        if (items.Count == 0) { DriversState = ItemStatus.Success; return; }
        if (askConfirm && !_app.Dialogs.Confirm(Loc.Get("Step_Drivers"), Loc.F("Restore_DriversConfirm", items.Count), danger: !_app.DryRun))
            return;

        await _app.OfferRestorePointAsync(ct);
        DriversState = ItemStatus.Running;

        List<DriverInfo> installed = [];
        try
        {
            _app.Operation.Report(null, Loc.Get("Restore_CheckingInstalledDrivers"));
            installed = await _app.Drivers.ListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warn(Loc.F("Restore_InstalledDriversUnknown", ex.Message));
        }

        var i = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            _app.Operation.Report(i++, items.Count, Loc.F("Restore_InstallingDriver", item.Name));
            var entry = (DriverManifestEntry)item.Tag!;
            if (DriverService.HasSameOrNewer(entry, installed, out var ver))
            {
                item.Status = ItemStatus.Skipped;
                item.Message = Loc.F("Restore_DriverNewerExists", ver);
                Log.Info(Loc.F("Restore_DriverSkippedLog", item.Name, ver, entry.Version));
                continue;
            }
            item.Status = ItemStatus.Running;
            var r = await _app.Drivers.InstallAsync(entry, Layout.DriversDir, _app.DryRun, ct);
            item.Status = r.Status;
            item.Message = r.Message;
            if (r.RebootRequired) RebootRequired = _progress.RebootRequired = true;
            if (r.Status == ItemStatus.Failed) Log.Error(Loc.F("Restore_DriverFailedLog", item.Name, r.Message));
            else if (r.Status == ItemStatus.Success) Log.Success(Loc.F("Restore_DriverOkLog", item.Name));
            _progress.Drivers[item.Key] = r.Status;
            SaveProgress();
        }

        var failed = items.Count(x => x.Status == ItemStatus.Failed);
        DriversState = failed > 0 ? ItemStatus.Failed : _app.DryRun ? ItemStatus.DryRun : ItemStatus.Success;
        if (!_app.DryRun && failed == 0) _progress.DriversDone = true;
        SaveProgress();
        if (RebootRequired) Log.Warn(Loc.Get("Restore_RebootNote"));
    }

    // ───────────── ADIM 2: Wi-Fi ─────────────

    [RelayCommand]
    private Task UnlockWifiAsync() => _app.Operation.RunAsync(Loc.Get("Step_Wifi"), async _ =>
    {
        await Task.Yield();
        UnlockWifi();
    });

    /// <summary>Parolayı sorar (en fazla 5 deneme), yedeği bellekte çözer ve profil listesini doldurur.</summary>
    private bool UnlockWifi()
    {
        if (_wifiPayload is not null) return true;
        string? error = null;
        for (var attempt = 1; attempt <= MaxPasswordAttempts; attempt++)
        {
            var pwd = _app.Dialogs.AskPassword(Loc.Get("Wifi_UnlockTitle"),
                Loc.F("Wifi_UnlockMessage", attempt, MaxPasswordAttempts), confirm: false, error);
            if (pwd is null) return false;
            try
            {
                _wifiPayload = WifiService.OpenBackup(Layout.WifiFile, pwd);
                break;
            }
            catch (WrongPasswordException)
            {
                error = Loc.F("Wifi_WrongPasswordAttempt", MaxPasswordAttempts - attempt);
                Log.Warn(Loc.Get("Wifi_WrongPasswordLog"));
            }
            finally
            {
                pwd = null;
            }
        }
        if (_wifiPayload is null)
        {
            _app.Dialogs.Error(Loc.Get("Wifi_UnlockTitle"), Loc.Get("Wifi_TooManyAttempts"));
            return false;
        }

        WifiItems.Clear();
        ConnectableProfiles.Clear();
        foreach (var p in _wifiPayload.Profiles)
        {
            WifiItems.Add(new StatusItem { Key = p.Name, Name = p.Name, Detail = p.Authentication, Tag = p });
            ConnectableProfiles.Add(p.Name);
        }
        ConnectProfile = ConnectableProfiles.FirstOrDefault();
        WifiUnlocked = true;
        Log.Success(Loc.F("Wifi_Unlocked", WifiItems.Count));
        return true;
    }

    [RelayCommand]
    private Task RunWifiAsync() =>
        _app.Operation.RunAsync(Loc.Get("Step_Wifi"), ct => WifiCoreAsync(ct, askConfirm: true));

    private async Task WifiCoreAsync(CancellationToken ct, bool askConfirm)
    {
        if (!UnlockWifi()) { WifiState = ItemStatus.Skipped; return; }
        var items = WifiItems.Where(i => i.IsSelected).ToList();
        if (items.Count == 0) { WifiState = ItemStatus.Skipped; return; }
        if (askConfirm && !_app.Dialogs.Confirm(Loc.Get("Step_Wifi"), Loc.F("Restore_WifiConfirm", items.Count)))
            return;

        WifiState = ItemStatus.Running;
        _app.Operation.Report(null, Loc.Get("Restore_AddingWifi"));
        var profiles = items.Select(i => (WifiPayloadProfile)i.Tag!).ToList();
        await _app.Wifi.AddProfilesAsync(profiles, _app.DryRun, (p, status, msg) =>
        {
            var item = items.FirstOrDefault(i => ReferenceEquals(i.Tag, p));
            if (item is null) return;
            item.Status = status;
            item.Message = msg;
        }, ct);

        var failed = items.Count(i => i.Status == ItemStatus.Failed);
        WifiState = failed > 0 ? ItemStatus.Failed : _app.DryRun ? ItemStatus.DryRun : ItemStatus.Success;
        if (!_app.DryRun && failed < items.Count) _progress.WifiDone = true;
        SaveProgress();
        await CheckInternetCoreAsync(ct, tryConnect: !_app.DryRun);
    }

    [RelayCommand]
    private Task CheckInternetAsync() =>
        _app.Operation.RunAsync(Loc.Get("Restore_CheckInternet"), ct => CheckInternetCoreAsync(ct, tryConnect: false));

    private async Task CheckInternetCoreAsync(CancellationToken ct, bool tryConnect)
    {
        _app.Operation.Report(null, Loc.Get("Restore_CheckInternet"));
        InternetOk = await NetworkService.HasInternetAsync(ct);
        if (!InternetOk && tryConnect && ConnectableProfiles.Count > 0)
        {
            // Birkaç saniye Windows'un kendiliğinden bağlanmasını bekle
            await Task.Delay(4000, ct);
            InternetOk = await NetworkService.HasInternetAsync(ct);
        }
        InternetStatus = Loc.Get(InternetOk ? "Restore_InternetOk" : "Restore_InternetMissing");
        if (InternetOk) Log.Success(InternetStatus);
        else Log.Warn(InternetStatus);
    }

    [RelayCommand]
    private Task ConnectWifiAsync() => _app.Operation.RunAsync(Loc.Get("Restore_Connecting"), async ct =>
    {
        if (string.IsNullOrEmpty(ConnectProfile)) return;
        if (_app.DryRun)
        {
            Log.Info(Loc.F("Dry_WifiConnect", ConnectProfile));
            return;
        }
        var ok = await _app.Wifi.ConnectAsync(ConnectProfile, ct);
        if (!ok) Log.Warn(Loc.Get("Restore_ConnectFailed"));
        await Task.Delay(5000, ct);
        await CheckInternetCoreAsync(ct, tryConnect: false);
    });

    // ───────────── ADIM 3: Uygulamalar ─────────────

    [RelayCommand]
    private Task RunAppsAsync() =>
        _app.Operation.RunAsync(Loc.Get("Step_Apps"), ct => AppsCoreAsync(ct, askConfirm: true, onlyFailed: false));

    [RelayCommand]
    private Task RetryFailedAsync() => _app.Operation.RunAsync(Loc.Get("Restore_RetryFailed"), async ct =>
    {
        var failedDrivers = DriverItems.Where(i => i.Status == ItemStatus.Failed).ToList();
        foreach (var d in failedDrivers) { d.IsSelected = true; d.Status = ItemStatus.Pending; }
        if (failedDrivers.Count > 0) await DriversCoreAsync(ct, askConfirm: false);
        if (AppItems.Any(i => i.Status == ItemStatus.Failed)) await AppsCoreAsync(ct, askConfirm: false, onlyFailed: true);
        var failedWifi = WifiItems.Where(i => i.Status == ItemStatus.Failed).ToList();
        if (failedWifi.Count > 0)
        {
            foreach (var w in WifiItems) w.IsSelected = failedWifi.Contains(w);
            await WifiCoreAsync(ct, askConfirm: false);
        }
        FinishRun();
    });

    private async Task AppsCoreAsync(CancellationToken ct, bool askConfirm, bool onlyFailed)
    {
        var items = AppItems.Where(i => onlyFailed
            ? i.Status == ItemStatus.Failed
            : i.IsSelected && i.Status is not (ItemStatus.Success or ItemStatus.AlreadyInstalled)).ToList();
        if (items.Count == 0) { AppsState = ItemStatus.Success; return; }

        if (!await EnsureWingetAsync(ct)) { AppsState = ItemStatus.Skipped; return; }

        _app.Operation.Report(null, Loc.Get("Restore_CheckInternet"));
        if (!await NetworkService.HasInternetAsync(ct))
        {
            AppsState = ItemStatus.Skipped;
            InternetStatus = Loc.Get("Restore_InternetMissing");
            Log.Warn(Loc.Get("Restore_AppsSkippedNoInternet"));
            _app.Dialogs.Warn(Loc.Get("Step_Apps"), Loc.Get("Restore_AppsSkippedNoInternet"));
            return;
        }

        if (askConfirm && !_app.Dialogs.Confirm(Loc.Get("Step_Apps"), Loc.F("Restore_AppsConfirm", items.Count)))
            return;

        AppsState = ItemStatus.Running;
        _app.Operation.Report(null, Loc.Get("Catalog_DetectingInstalled"));
        HashSet<string> installed;
        try { installed = await _app.Winget.GetInstalledIdsAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Log.Warn(ex.Message);
        }

        var n = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            _app.Operation.Report(n++, items.Count, Loc.F("Restore_InstallingApp", item.Name, n, items.Count));
            var pkg = (WingetPackageRef)item.Tag!;
            if (installed.Contains(pkg.Id))
            {
                item.Status = ItemStatus.AlreadyInstalled;
                item.Message = Loc.Get("Winget_AlreadyInstalled");
            }
            else
            {
                item.Status = ItemStatus.Running;
                var r = await _app.Winget.InstallAsync(pkg.Id, pkg.Source, _app.DryRun, ct);
                item.Status = r.Status;
                item.Message = r.Message;
                if (r.Status == ItemStatus.Failed) Log.Error(Loc.F("Restore_AppFailedLog", pkg.Id, r.Message));
                else if (r.Status == ItemStatus.Success) Log.Success(Loc.F("Restore_AppOkLog", pkg.Id));
            }
            _progress.Apps[pkg.Id] = item.Status;
            SaveProgress();
        }

        var failed = AppItems.Count(i => i.Status == ItemStatus.Failed);
        AppsState = failed > 0 ? ItemStatus.Failed : _app.DryRun ? ItemStatus.DryRun : ItemStatus.Success;
        if (!_app.DryRun && failed == 0) _progress.AppsDone = true;
        SaveProgress();
    }

    private async Task<bool> EnsureWingetAsync(CancellationToken ct)
    {
        if (await _app.Winget.IsAvailableAsync(ct)) return true;
        if (!_app.Dialogs.Confirm(Loc.Get("Winget_MissingTitle"), Loc.Get("Winget_MissingQuestion"),
                Loc.Get("Winget_TryInstall"), Loc.Get("Btn_Skip")))
            return false;
        _app.Operation.Report(null, Loc.Get("Winget_Installing"));
        var ok = await _app.Winget.TryInstallAsync(_app.DryRun, ct);
        if (!ok) _app.Dialogs.Warn(Loc.Get("Winget_MissingTitle"), Loc.Get("Winget_InstallFailed"));
        return ok;
    }

    // ───────────── ADIM 4: Save'ler ─────────────

    private bool EnsureLudusaviFromBackup()
    {
        if (LudusaviService.IsInstalled) return true;
        var fromBackup = Path.Combine(Layout.Root, "Tools", "ludusavi.exe");
        if (!File.Exists(fromBackup)) return false;
        Directory.CreateDirectory(AppPaths.ToolsDir);
        File.Copy(fromBackup, LudusaviService.ExePath, overwrite: false);
        return true;
    }

    private string? OldUserProfile =>
        Json.Read<LudusaviBackupInfo>(Layout.LudusaviInfoFile)?.UserProfile ?? _manifest?.UserProfile;

    [RelayCommand]
    private Task PreviewSavesAsync() => _app.Operation.RunAsync(Loc.Get("Restore_SavesPreview"), PreviewSavesCoreAsync);

    private async Task PreviewSavesCoreAsync(CancellationToken ct)
    {
        if (!Directory.Exists(Layout.LudusaviDir)) return;
        if (!EnsureLudusaviFromBackup())
        {
            if (!_app.Dialogs.Confirm(Loc.Get("Ludusavi_DownloadTitle"), Loc.Get("Ludusavi_DownloadQuestion"),
                    Loc.Get("Btn_Download"), Loc.Get("Btn_Cancel")))
                throw new OperationCanceledException();
            await _app.Ludusavi.DownloadLatestAsync(ct);
        }
        _app.Operation.Report(null, Loc.Get("Restore_SavesPreview"));
        var games = await _app.Ludusavi.RestorePreviewAsync(Layout.LudusaviDir, OldUserProfile, ct);
        GameItems.Clear();
        foreach (var g in games)
        {
            GameItems.Add(new GameItem(g)
            {
                Message = g.ConflictCount > 0 ? Loc.F("Restore_GameConflict", g.ConflictCount) : ""
            });
        }
        Log.Info(Loc.F("Restore_SavesFound", games.Count, games.Count(g => g.ConflictCount > 0)));
    }

    [RelayCommand]
    private Task RunSavesAsync() =>
        _app.Operation.RunAsync(Loc.Get("Step_Saves"), ct => SavesCoreAsync(ct, askConfirm: true));

    private async Task SavesCoreAsync(CancellationToken ct, bool askConfirm)
    {
        SavesState = ItemStatus.Running;
        var policy = SelectedPolicy.Value;
        if (Directory.Exists(Layout.LudusaviDir) && GameItems.Count == 0) await PreviewSavesCoreAsync(ct);

        var selected = GameItems.Where(g => g.IsSelected).ToList();
        var conflicts = selected.Where(g => g.HasConflict).ToList();
        if (askConfirm || conflicts.Count > 0)
        {
            var msg = Loc.F("Restore_SavesConfirm", selected.Count, CustomItems.Count(c => c.IsSelected));
            if (conflicts.Count > 0) msg += "\n\n" + Loc.F("Restore_SavesConflictNote", conflicts.Count, SelectedPolicy.Title);
            if (!_app.Dialogs.Confirm(Loc.Get("Step_Saves"), msg, danger: conflicts.Count > 0 && policy == ConflictPolicy.UseBackup))
            {
                SavesState = ItemStatus.Pending;
                return;
            }
        }

        // Çakışma politikası (oyun bazında)
        var times = LudusaviService.ReadBackupTimes(Layout.LudusaviDir);
        var toRestore = new List<GameItem>();
        foreach (var g in selected)
        {
            if (!g.HasConflict || policy == ConflictPolicy.UseBackup) { toRestore.Add(g); continue; }
            if (policy == ConflictPolicy.KeepExisting)
            {
                g.Status = ItemStatus.Skipped;
                g.Message = Loc.Get("Restore_GameKeptExisting");
                continue;
            }
            // KeepNewer
            if (times.TryGetValue(g.Name, out var t) && LudusaviService.LiveIsNewer(g.Save, t))
            {
                g.Status = ItemStatus.Skipped;
                g.Message = Loc.Get("Restore_GameLiveNewer");
            }
            else toRestore.Add(g);
        }

        if (toRestore.Count > 0)
        {
            foreach (var g in toRestore) g.Status = ItemStatus.Running;
            _app.Operation.Report(null, Loc.F("Restore_RestoringGames", toRestore.Count));
            var r = await _app.Ludusavi.RestoreAsync(toRestore.Select(g => g.Name).ToList(), Layout.LudusaviDir,
                OldUserProfile, _app.DryRun, ct);
            foreach (var g in toRestore)
            {
                var err = r.Errors.FirstOrDefault(e => e.StartsWith(g.Name + ":", StringComparison.Ordinal));
                g.Status = err is not null ? ItemStatus.Failed : _app.DryRun ? ItemStatus.DryRun : ItemStatus.Success;
                g.Message = err ?? "";
            }
            Log.Success(Loc.F("Restore_GamesDone", r.Games, SafePath.FormatBytes(r.Bytes)));
        }

        var customSelected = CustomItems.Where(c => c.IsSelected).ToList();
        for (var i = 0; i < customSelected.Count; i++)
        {
            var c = customSelected[i];
            _app.Operation.Report(i, customSelected.Count, c.Name);
            c.Status = ItemStatus.Running;
            try
            {
                var stats = await _app.CustomFolders.RestoreAsync((CustomFolderEntry)c.Tag!, Layout.CustomDir, policy, _app.DryRun, ct);
                c.Status = _app.DryRun ? ItemStatus.DryRun : stats.Errors > 0 ? ItemStatus.Failed : ItemStatus.Success;
                c.Message = _app.DryRun ? "" : Loc.F("Custom_RestoreStats", stats.Files, stats.Skipped, stats.Errors);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                c.Status = ItemStatus.Failed;
                c.Message = ex.Message;
                Log.Error(c.Name, ex);
            }
        }

        var failed = GameItems.Count(g => g.Status == ItemStatus.Failed) + customSelected.Count(c => c.Status == ItemStatus.Failed);
        SavesState = failed > 0 ? ItemStatus.Failed : _app.DryRun ? ItemStatus.DryRun : ItemStatus.Success;
        if (!_app.DryRun && failed == 0) _progress.SavesDone = true;
        SaveProgress();
    }

    // ───────────── ADIM 5: Windows ayarları ─────────────

    [RelayCommand]
    private Task RunSettingsAsync() =>
        _app.Operation.RunAsync(Loc.Get("Step_Settings"), ct => SettingsCoreAsync(ct, askConfirm: true));

    private async Task SettingsCoreAsync(CancellationToken ct, bool askConfirm)
    {
        var ids = SettingItems.Where(s => s.IsSelected).Select(s => s.Key).ToList();
        if (ids.Count == 0) { SettingsState = ItemStatus.Skipped; return; }
        var backup = WindowsSettingsService.Load(Layout.WindowsSettingsFile)
                     ?? throw new InvalidOperationException(Loc.Get("WinSet_BackupUnreadable"));

        if (!_app.Dialogs.Confirm(Loc.Get("Step_Settings"),
                Loc.F("Restore_SettingsConfirm", string.Join("\n  • ", SettingItems.Where(s => s.IsSelected).Select(s => s.Name)))))
        {
            SettingsState = ItemStatus.Pending;
            return;
        }

        await _app.OfferRestorePointAsync(ct);
        SettingsState = ItemStatus.Running;
        _app.Operation.Report(null, Loc.Get("Step_Settings"));
        var report = await Task.Run(() => _app.WindowsSettings.Apply(backup, ids, Layout.WindowsSettingsDir,
            WindowsSettingsViewModel.SnapshotsDir, _app.DryRun), ct);

        foreach (var s in SettingItems.Where(s => s.IsSelected))
        {
            var title = s.Name;
            if (report.Failed.Any(f => f.StartsWith(title, StringComparison.Ordinal)))
            {
                s.Status = ItemStatus.Failed;
                s.Message = report.Failed.First(f => f.StartsWith(title, StringComparison.Ordinal));
            }
            else if (report.Skipped.Any(f => f.StartsWith(title, StringComparison.Ordinal)))
            {
                s.Status = ItemStatus.Skipped;
                s.Message = report.Skipped.First(f => f.StartsWith(title, StringComparison.Ordinal));
            }
            else s.Status = _app.DryRun ? ItemStatus.DryRun : ItemStatus.Success;
        }
        SettingsState = report.Failed.Count > 0 ? ItemStatus.Failed : _app.DryRun ? ItemStatus.DryRun : ItemStatus.Success;
        if (!_app.DryRun && report.Failed.Count == 0) _progress.SettingsDone = true;
        SaveProgress();
        WindowsSettingsViewModel.ShowReport(_app, report);
    }

    [RelayCommand]
    private void ResetProgress()
    {
        if (_layout is null) return;
        if (!_app.Dialogs.Confirm(Loc.Get("Restore_ResetTitle"), Loc.Get("Restore_ResetQuestion"))) return;
        _progress = new RestoreProgress();
        SaveProgress();
        if (SelectedCandidate is not null) LoadBackup(SelectedCandidate);
    }
}
