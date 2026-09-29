using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rebornix.Helpers;
using Rebornix.Models;
using Rebornix.Services;

namespace Rebornix.ViewModels;

public sealed record NavItem(string Key, string Title, string Glyph);

public sealed partial class MainViewModel : ObservableObject
{
    private readonly Dictionary<string, object> _pages;

    public MainViewModel(AppServices app)
    {
        App = app;
        Drivers = new DriversViewModel(app);
        Wifi = new WifiViewModel(app);
        WinSettings = new WindowsSettingsViewModel(app);
        Backup = new BackupViewModel(app, Drivers, Wifi, WinSettings);
        Restore = new RestoreViewModel(app);
        Catalog = new CatalogViewModel(app);
        Onboarding = new OnboardingViewModel(app, Navigate);
        Settings = new SettingsViewModel(app, Onboarding.Open);
        Logs = new LogsViewModel();
        Home = new HomeViewModel(app, Navigate, Onboarding.Open);

        _pages = new Dictionary<string, object>
        {
            ["home"] = Home, ["backup"] = Backup, ["restore"] = Restore, ["catalog"] = Catalog,
            ["drivers"] = Drivers, ["wifi"] = Wifi, ["winsettings"] = WinSettings,
            ["settings"] = Settings, ["logs"] = Logs
        };

        NavItems =
        [
            new("home", Loc.Get("Nav_Home"), ""),
            new("backup", Loc.Get("Nav_Backup"), ""),
            new("restore", Loc.Get("Nav_Restore"), ""),
            new("catalog", Loc.Get("Nav_Catalog"), ""),
            new("drivers", Loc.Get("Nav_Drivers"), ""),
            new("wifi", Loc.Get("Nav_Wifi"), ""),
            new("winsettings", Loc.Get("Nav_WinSettings"), ""),
            new("settings", Loc.Get("Nav_Settings"), ""),
            new("logs", Loc.Get("Nav_Logs"), "")
        ];
        _selectedNav = NavItems[0];
        _currentPage = Home;

        app.Settings.Changed += () => OnPropertyChanged(nameof(IsDryRun));
        Log.EntryAdded += e => LastLog = e;
    }

    public AppServices App { get; }
    public OperationService Operation => App.Operation;
    public HomeViewModel Home { get; }
    public BackupViewModel Backup { get; }
    public RestoreViewModel Restore { get; }
    public CatalogViewModel Catalog { get; }
    public DriversViewModel Drivers { get; }
    public WifiViewModel Wifi { get; }
    public WindowsSettingsViewModel WinSettings { get; }
    public SettingsViewModel Settings { get; }
    public LogsViewModel Logs { get; }
    public OnboardingViewModel Onboarding { get; }

    public IReadOnlyList<NavItem> NavItems { get; }
    public ObservableCollection<LogEntry> LogEntries => Log.Entries;
    public bool IsDryRun => App.DryRun;
    [ObservableProperty] private bool _isAdmin = Elevation.IsAdmin;

    [ObservableProperty] private NavItem _selectedNav;
    [ObservableProperty] private object _currentPage;
    [ObservableProperty] private bool _isLogPanelOpen = true;
    [ObservableProperty] private LogEntry? _lastLog;

    partial void OnSelectedNavChanged(NavItem value)
    {
        if (value is not null && _pages.TryGetValue(value.Key, out var page))
        {
            CurrentPage = page;
            if (page is IPageActivated a) a.OnActivated();
        }
    }

    public void Navigate(string key)
    {
        var item = NavItems.FirstOrDefault(n => n.Key == key);
        if (item is not null) SelectedNav = item;
    }

    [RelayCommand]
    private void ToggleLogPanel() => IsLogPanelOpen = !IsLogPanelOpen;
}

/// <summary>Sayfa ilk açıldığında (veya her açılışta) bir şey yüklemek isteyen sayfalar.</summary>
public interface IPageActivated
{
    void OnActivated();
}
