using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rebornix.Helpers;
using Rebornix.Models;
using Rebornix.Services;

namespace Rebornix.ViewModels;

/// <summary>Sol menü öğesi. Her sayfanın kendi rengi vardır (ikon kutucuğu ve seçili hali).</summary>
public sealed record NavItem(string Key, string Title, string Glyph, string Hex)
{
    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    /// <summary>İkon rengi.</summary>
    public Brush Accent { get; } = Freeze(new SolidColorBrush(C(Hex)));

    /// <summary>Seçili değilken ikon kutucuğunun yarı saydam zemini.</summary>
    public Brush Tint { get; } = Freeze(new SolidColorBrush(Color.FromArgb(0x26, C(Hex).R, C(Hex).G, C(Hex).B)));

    /// <summary>Seçiliyken kutucuğu dolduran gradyan (sayfa rengi → marka indigosu).</summary>
    public Brush Gradient { get; } = Freeze(new LinearGradientBrush(C(Hex), Color.FromRgb(0x63, 0x66, 0xF1), 45));
}

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
        Welcome = new WelcomeViewModel(app, Onboarding.Open);
        Appearance = new AppearanceViewModel(app, () => SelectedNav?.Key, () => Welcome.IsOpen);
        Welcome.Appearance = Appearance;
        Settings = new SettingsViewModel(app, Onboarding.Open, Welcome.Open, Appearance);
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
            new("home", Loc.Get("Nav_Home"), "", "#A78BFA"),
            new("backup", Loc.Get("Nav_Backup"), "", "#60A5FA"),
            new("restore", Loc.Get("Nav_Restore"), "", "#34D399"),
            new("catalog", Loc.Get("Nav_Catalog"), "", "#F472B6"),
            new("drivers", Loc.Get("Nav_Drivers"), "", "#FBBF24"),
            new("wifi", Loc.Get("Nav_Wifi"), "", "#22D3EE"),
            new("winsettings", Loc.Get("Nav_WinSettings"), "", "#818CF8"),
            new("settings", Loc.Get("Nav_Settings"), "", "#94A3B8"),
            new("logs", Loc.Get("Nav_Logs"), "", "#FB923C")
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
    public WelcomeViewModel Welcome { get; }
    public AppearanceViewModel Appearance { get; }

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
