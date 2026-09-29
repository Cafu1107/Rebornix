using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rebornix.Helpers;
using Rebornix.Models;
using Rebornix.Services;

namespace Rebornix.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject, IPageActivated
{
    private readonly AppServices _app;
    private readonly Action _showGuide;

    public SettingsViewModel(AppServices app, Action showGuide)
    {
        _app = app;
        _showGuide = showGuide;
        _dryRun = app.Settings.Current.DryRun;
        _offerRestorePoint = app.Settings.Current.OfferRestorePoint;
    }

    [ObservableProperty] private bool _dryRun;
    [ObservableProperty] private bool _offerRestorePoint;
    [ObservableProperty] private string _wingetStatus = "";
    [ObservableProperty] private string _ludusaviStatus = "";

    public string BaseDir => AppPaths.BaseDir;
    public string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "";
    public bool IsAdmin => Elevation.IsAdmin;

    partial void OnDryRunChanged(bool value)
    {
        _app.Settings.Current.DryRun = value;
        _app.Settings.Save();
        Log.Info(Loc.Get(value ? "Settings_DryOn" : "Settings_DryOff"));
    }

    partial void OnOfferRestorePointChanged(bool value)
    {
        _app.Settings.Current.OfferRestorePoint = value;
        _app.Settings.Save();
    }

    public void OnActivated() => _ = RefreshStatusAsync();

    [RelayCommand]
    private void ShowGuide() => _showGuide();

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        WingetStatus = await _app.Winget.IsAvailableAsync() ? Loc.Get("Home_WingetOk") : Loc.Get("Home_WingetMissing");
        var v = await _app.Ludusavi.GetVersionAsync(CancellationToken.None);
        LudusaviStatus = v is null ? Loc.Get("Saves_LudusaviMissing") : v;
    }

    [RelayCommand]
    private Task InstallWingetAsync() => _app.Operation.RunAsync(Loc.Get("Winget_Installing"), async ct =>
    {
        if (await _app.Winget.IsAvailableAsync(ct))
        {
            _app.Dialogs.Info(Loc.Get("Winget_MissingTitle"), Loc.Get("Home_WingetOk"));
            return;
        }
        var ok = await _app.Winget.TryInstallAsync(_app.DryRun, ct);
        if (!ok) _app.Dialogs.Warn(Loc.Get("Winget_MissingTitle"), Loc.Get("Winget_InstallFailed"));
        await RefreshStatusAsync();
    });

    [RelayCommand]
    private Task UpdateLudusaviAsync() => _app.Operation.RunAsync(Loc.Get("Ludusavi_DownloadTitle"), async ct =>
    {
        await _app.Ludusavi.DownloadLatestAsync(ct);
        await RefreshStatusAsync();
    });

    [RelayCommand]
    private Task CreateRestorePointAsync() => _app.Operation.RunAsync(Loc.Get("RestorePoint_Title"), async ct =>
    {
        _app.Operation.Report(null, Loc.Get("RestorePoint_Creating"));
        var (ok, msg) = await RestorePointService.CreateAsync("Rebornix", _app.DryRun, ct);
        if (ok) Log.Success(msg);
        else Log.Warn(msg);
        _app.Dialogs.Show(ok ? DialogKind.Info : DialogKind.Warning, Loc.Get("RestorePoint_Title"),
            string.IsNullOrEmpty(msg) ? Loc.Get("Dry_Banner") : msg, Loc.Get("Btn_Ok"));
    });

    [RelayCommand]
    private void OpenFolder(string? which)
    {
        var dir = which switch
        {
            "data" => AppPaths.DataDir,
            "logs" => AppPaths.LogsDir,
            "tools" => AppPaths.ToolsDir,
            _ => AppPaths.BaseDir
        };
        Directory.CreateDirectory(dir);
        Shell.OpenFolder(dir);
    }
}

public sealed partial class LogsViewModel : ObservableObject, IPageActivated
{
    public ObservableCollection<LogEntry> Entries => Log.Entries;
    public ObservableCollection<string> Files { get; } = [];

    [ObservableProperty] private bool _showDebug;

    public void OnActivated() => RefreshFiles();

    [RelayCommand]
    private void RefreshFiles()
    {
        Files.Clear();
        if (!Directory.Exists(AppPaths.LogsDir)) return;
        foreach (var f in Directory.EnumerateFiles(AppPaths.LogsDir, "*.log").OrderByDescending(f => f))
            Files.Add(Path.GetFileName(f));
    }

    [RelayCommand]
    private void OpenLogFile(string? name)
    {
        var file = string.IsNullOrEmpty(name) ? Log.CurrentFile : SafePath.Combine(AppPaths.LogsDir, name);
        if (File.Exists(file)) Shell.OpenFile(file);
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        Directory.CreateDirectory(AppPaths.LogsDir);
        Shell.OpenFolder(AppPaths.LogsDir);
    }

    [RelayCommand]
    private void ClearView() => Log.Entries.Clear();
}
