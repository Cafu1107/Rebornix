using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rebornix.Helpers;
using Rebornix.Models;
using Rebornix.Services;

namespace Rebornix.ViewModels;

/// <summary>Wi-Fi şifreleri yedeği: varsayılan KAPALI, kullanıcı uyarıyı onaylayarak açar.</summary>
public sealed partial class WifiViewModel : ObservableObject
{
    private readonly AppServices _app;
    private bool _suppress;

    public WifiViewModel(AppServices app) => _app = app;

    public ObservableCollection<WifiProfileItem> Profiles { get; } = [];

    [ObservableProperty] private bool _isBackupEnabled;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private string _summary = "";

    public int SelectedCount => Profiles.Count(p => p.IsSelected);
    public string SelectionText => Loc.F("Wifi_Selection", SelectedCount, Profiles.Count);

    partial void OnIsBackupEnabledChanged(bool value)
    {
        if (_suppress) return;
        if (!value)
        {
            Log.Info(Loc.Get("Wifi_Disabled"));
            return;
        }
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var ok = _app.Dialogs.Confirm(Loc.Get("Wifi_EnableTitle"), Loc.Get("Wifi_EnableWarning"),
                Loc.Get("Wifi_EnableYes"), Loc.Get("Btn_Cancel"));
            if (!ok)
            {
                _suppress = true;
                IsBackupEnabled = false;
                _suppress = false;
                return;
            }
            Log.Info(Loc.Get("Wifi_Enabled"));
            if (!IsLoaded) LoadCommand.Execute(null);
        });
    }

    [RelayCommand]
    private Task LoadAsync() => _app.Operation.RunAsync(Loc.Get("Wifi_Loading"), LoadCoreAsync);

    public async Task LoadCoreAsync(CancellationToken ct)
    {
        _app.Operation.Report(null, Loc.Get("Wifi_Loading"));
        var list = await _app.Wifi.ReadProfilesAsync(ct);
        foreach (var p in Profiles) p.PropertyChanged -= OnProfileChanged;
        Profiles.Clear();
        foreach (var p in list)
        {
            var item = new WifiProfileItem(p);
            item.PropertyChanged += OnProfileChanged;
            Profiles.Add(item);
        }
        IsLoaded = true;
        Summary = Loc.F("Wifi_Found", Profiles.Count);
        // Sadece profil sayısı loglanır; ad ve şifre değil
        Log.Info(Summary);
        RaiseCounts();
    }

    private void OnProfileChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WifiProfileItem.IsSelected)) RaiseCounts();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionText));
    }

    [RelayCommand]
    private void HideAllPasswords()
    {
        foreach (var p in Profiles) p.IsPasswordVisible = false;
    }

    public List<string> SelectedNames() => Profiles.Where(p => p.IsSelected).Select(p => p.Name).ToList();
}
