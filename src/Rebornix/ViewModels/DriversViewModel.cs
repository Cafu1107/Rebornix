using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rebornix.Helpers;
using Rebornix.Models;
using Rebornix.Services;

namespace Rebornix.ViewModels;

public sealed class DriverGroup(string name, bool isCritical, IReadOnlyList<DriverItem> items)
{
    public string Name { get; } = name;
    public bool IsCritical { get; } = isCritical;
    public IReadOnlyList<DriverItem> Items { get; } = items;
    public string CountText => Loc.F("Drivers_GroupCount", Items.Count);
}

public sealed partial class DriversViewModel : ObservableObject, IPageActivated
{
    private readonly AppServices _app;
    private bool _suppressWarning;

    public DriversViewModel(AppServices app) => _app = app;

    public List<DriverItem> Items { get; private set; } = [];
    public ObservableCollection<DriverGroup> Groups { get; } = [];

    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _summary = "";

    public int SelectedCount => Items.Count(i => i.IsSelected);
    public long SelectedBytes => Items.Where(i => i.IsSelected).Sum(i => i.Info.SizeBytes);
    public string SelectionText => Loc.F("Drivers_Selection", SelectedCount, Items.Count, SafePath.FormatBytes(SelectedBytes));

    public void OnActivated()
    {
        if (!IsLoaded && !IsLoading && !_app.Operation.IsBusy) LoadCommand.Execute(null);
    }

    [RelayCommand]
    private Task LoadAsync() => _app.Operation.RunAsync(Loc.Get("Drivers_Loading"), LoadCoreAsync);

    /// <summary>Yedekleme sırasında da çağrılır (liste henüz yüklenmediyse).</summary>
    public async Task LoadCoreAsync(CancellationToken ct)
    {
        IsLoading = true;
        try
        {
            _app.Operation.Report(null, Loc.Get("Drivers_Loading"));
            var list = await _app.Drivers.ListAsync(ct);
            foreach (var old in Items) old.PropertyChanged -= OnItemChanged;
            Items = list.Select(d => new DriverItem(d)).ToList();
            foreach (var i in Items) i.PropertyChanged += OnItemChanged;

            Groups.Clear();
            foreach (var g in Items.GroupBy(i => i.Category).OrderBy(g => (int)g.Key))
            {
                Groups.Add(new DriverGroup(DriverCategories.DisplayName(g.Key), g.Key == DriverCategory.Network,
                    g.OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList()));
            }
            IsLoaded = true;
            Summary = Loc.F("Drivers_Found", Items.Count, Items.Count(i => i.IsCritical));
            Log.Info(Summary);
            RaiseCounts();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DriverItem.IsSelected) || sender is not DriverItem item) return;
        RaiseCounts();
        if (item.IsCritical && !item.IsSelected && !_suppressWarning)
        {
            // Diyaloğu bağlama güncellemesi bittikten sonra göster
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                var remove = _app.Dialogs.Confirm(Loc.Get("Drivers_CriticalTitle"),
                    Loc.F("Drivers_CriticalWarning", item.DisplayName),
                    Loc.Get("Drivers_CriticalRemove"), Loc.Get("Drivers_CriticalKeep"), danger: true);
                if (!remove)
                {
                    _suppressWarning = true;
                    item.IsSelected = true;
                    _suppressWarning = false;
                }
                else Log.Warn(Loc.F("Drivers_CriticalRemovedLog", item.DisplayName));
            });
        }
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedBytes));
        OnPropertyChanged(nameof(SelectionText));
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var i in Items) i.IsSelected = true;
    }

    /// <summary>Kritik (ağ) sürücüler hariç tüm işaretleri kaldırır.</summary>
    [RelayCommand]
    private void SelectOnlyCritical()
    {
        _suppressWarning = true;
        foreach (var i in Items) i.IsSelected = i.IsCritical;
        _suppressWarning = false;
    }
}
