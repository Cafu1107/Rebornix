using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rebornix.Helpers;
using Rebornix.Services;

namespace Rebornix.ViewModels;

public sealed partial class HomeViewModel : ObservableObject, IPageActivated
{
    private readonly AppServices _app;
    private readonly Action<string> _navigate;
    private readonly Action _showGuide;
    private bool _checked;

    public HomeViewModel(AppServices app, Action<string> navigate, Action showGuide)
    {
        _app = app;
        _navigate = navigate;
        _showGuide = showGuide;
        app.Settings.Changed += RefreshSummary;
        RefreshSummary();
    }

    public ObservableCollection<string> SummaryLines { get; } = [];
    [ObservableProperty] private string _summaryTitle = "";
    [ObservableProperty] private string _summaryTime = "";
    [ObservableProperty] private bool _hasSummary;

    [ObservableProperty] private string _adminStatus = "";
    [ObservableProperty] private string _wingetStatus = Loc.Get("Home_Checking");
    [ObservableProperty] private string _internetStatus = Loc.Get("Home_Checking");
    [ObservableProperty] private bool _adminOk;
    [ObservableProperty] private bool _wingetOk;
    [ObservableProperty] private bool _internetOk;

    public bool IsDryRun => _app.DryRun;

    [RelayCommand] private void GoBackup() => _navigate("backup");
    [RelayCommand] private void GoRestore() => _navigate("restore");
    [RelayCommand] private void GoCatalog() => _navigate("catalog");
    [RelayCommand] private void ShowGuide() => _showGuide();

    private void RefreshSummary()
    {
        var s = _app.Settings.Current;
        SummaryLines.Clear();
        foreach (var l in s.LastSummary) SummaryLines.Add(l);
        HasSummary = SummaryLines.Count > 0;
        SummaryTitle = string.IsNullOrEmpty(s.LastSummaryTitle) ? Loc.Get("Home_NoSummary") : s.LastSummaryTitle;
        SummaryTime = s.LastSummaryTime?.ToString("dd.MM.yyyy HH:mm") ?? "";
        OnPropertyChanged(nameof(IsDryRun));
    }

    public void OnActivated()
    {
        if (_checked) return;
        _checked = true;
        _ = CheckEnvironmentAsync();
    }

    [RelayCommand]
    private async Task CheckEnvironmentAsync()
    {
        AdminOk = Elevation.IsAdmin;
        AdminStatus = Loc.Get(AdminOk ? "Home_AdminOk" : "Home_AdminMissing");
        try
        {
            WingetOk = await _app.Winget.IsAvailableAsync();
            WingetStatus = Loc.Get(WingetOk ? "Home_WingetOk" : "Home_WingetMissing");
        }
        catch
        {
            WingetOk = false;
            WingetStatus = Loc.Get("Home_WingetMissing");
        }
        try
        {
            InternetOk = await NetworkService.HasInternetAsync();
        }
        catch
        {
            InternetOk = false;
        }
        InternetStatus = Loc.Get(InternetOk ? "Home_InternetOk" : "Home_InternetMissing");
    }
}
