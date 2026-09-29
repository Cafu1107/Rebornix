using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rebornix.Helpers;
using Rebornix.Models;
using Rebornix.Services;

namespace Rebornix.ViewModels;

public sealed partial class CatalogViewModel : ObservableObject, IPageActivated
{
    private readonly AppServices _app;
    private Dictionary<string, List<string>> _profiles = new();
    private bool _loaded;

    public CatalogViewModel(AppServices app)
    {
        _app = app;
        View = CollectionViewSource.GetDefaultView(Items);
        View.Filter = Filter;
    }

    public ObservableCollection<CatalogItem> Items { get; } = [];
    public ICollectionView View { get; }
    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<string> ProfileNames { get; } = [];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedCategory = Loc.Get("Catalog_AllCategories");
    [ObservableProperty] private string? _selectedProfile;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _hasFailures;

    public int SelectedCount => Items.Count(i => i.IsSelected && !i.IsInstalled);
    public string SelectionText => Loc.F("Catalog_Selection", SelectedCount, Items.Count, Items.Count(i => i.IsInstalled));

    partial void OnSearchTextChanged(string value) => View.Refresh();
    partial void OnSelectedCategoryChanged(string value) => View.Refresh();

    private bool Filter(object o)
    {
        if (o is not CatalogItem i) return false;
        if (SelectedCategory != Loc.Get("Catalog_AllCategories") && i.Category != SelectedCategory) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return i.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
               i.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
               i.Description.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    public void OnActivated()
    {
        if (_loaded) return;
        _loaded = true;
        LoadCatalog();
        // Başka bir işlem sürüyorsa uyarı göstermeden atla; kullanıcı yenile butonuyla tekrar tetikleyebilir
        if (!_app.Operation.IsBusy)
            _ = _app.Operation.RunAsync(Loc.Get("Catalog_DetectingInstalled"), DetectInstalledCoreAsync);
    }

    [RelayCommand]
    private void LoadCatalog()
    {
        var catalog = CatalogService.Load();
        foreach (var i in Items) i.PropertyChanged -= OnItemChanged;
        Items.Clear();
        foreach (var a in catalog.Apps)
        {
            var item = new CatalogItem(a);
            item.PropertyChanged += OnItemChanged;
            Items.Add(item);
        }
        Categories.Clear();
        Categories.Add(Loc.Get("Catalog_AllCategories"));
        foreach (var c in catalog.Categories) Categories.Add(c);
        SelectedCategory = Categories[0];

        _profiles = ProfileService.Load();
        RefreshProfileNames();
        RaiseCounts();
        Log.Info(Loc.F("Catalog_Loaded", Items.Count, AppPaths.CatalogFile));
    }

    private void RefreshProfileNames()
    {
        ProfileNames.Clear();
        foreach (var n in _profiles.Keys.OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase)) ProfileNames.Add(n);
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CatalogItem.IsSelected) or nameof(CatalogItem.IsInstalled)) RaiseCounts();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionText));
    }

    [RelayCommand]
    private Task DetectInstalledAsync() => _app.Operation.RunAsync(Loc.Get("Catalog_DetectingInstalled"), DetectInstalledCoreAsync);

    private async Task DetectInstalledCoreAsync(CancellationToken ct)
    {
        _app.Operation.Report(null, Loc.Get("Catalog_DetectingInstalled"));
        if (!await _app.Winget.IsAvailableAsync(ct))
        {
            StatusText = Loc.Get("Home_WingetMissing");
            return;
        }
        var installed = await _app.Winget.GetInstalledIdsAsync(ct);
        foreach (var i in Items)
        {
            i.IsInstalled = installed.Contains(i.Id);
            if (i.IsInstalled)
            {
                i.IsSelected = false;
                i.Status = ItemStatus.AlreadyInstalled;
            }
        }
        StatusText = Loc.F("Catalog_InstalledFound", Items.Count(i => i.IsInstalled));
        Log.Info(StatusText);
    }

    /// <summary>Görünen (filtrelenmiş) ve kurulu olmayan her şeyi seçer.</summary>
    [RelayCommand]
    private void SelectAll()
    {
        foreach (var i in View.Cast<CatalogItem>()) if (!i.IsInstalled) i.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var i in Items) i.IsSelected = false;
    }

    // ───────────── Profiller ─────────────

    [RelayCommand]
    private void SaveProfile()
    {
        var ids = Items.Where(i => i.IsSelected).Select(i => i.Id).ToList();
        if (ids.Count == 0)
        {
            _app.Dialogs.Info(Loc.Get("Catalog_ProfileTitle"), Loc.Get("Catalog_ProfileEmpty"));
            return;
        }
        var name = _app.Dialogs.AskText(Loc.Get("Catalog_ProfileTitle"), Loc.Get("Catalog_ProfileNamePrompt"), SelectedProfile ?? "");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (name.Length > 60) name = name[..60];
        if (_profiles.ContainsKey(name) &&
            !_app.Dialogs.Confirm(Loc.Get("Catalog_ProfileTitle"), Loc.F("Catalog_ProfileOverwrite", name)))
            return;
        _profiles[name] = ids;
        ProfileService.Save(_profiles);
        RefreshProfileNames();
        SelectedProfile = name;
        Log.Success(Loc.F("Catalog_ProfileSaved", name, ids.Count));
    }

    [RelayCommand]
    private void LoadProfile()
    {
        if (SelectedProfile is null || !_profiles.TryGetValue(SelectedProfile, out var ids)) return;
        var set = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        foreach (var i in Items) i.IsSelected = set.Contains(i.Id) && !i.IsInstalled;
        var unknown = ids.Where(id => Items.All(i => !string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase))).ToList();
        if (unknown.Count > 0) Log.Warn(Loc.F("Catalog_ProfileUnknownIds", string.Join(", ", unknown)));
        Log.Info(Loc.F("Catalog_ProfileLoaded", SelectedProfile));
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is null) return;
        if (!_app.Dialogs.Confirm(Loc.Get("Catalog_ProfileTitle"), Loc.F("Catalog_ProfileDeleteQuestion", SelectedProfile), danger: true))
            return;
        _profiles.Remove(SelectedProfile);
        ProfileService.Save(_profiles);
        RefreshProfileNames();
        SelectedProfile = null;
    }

    [RelayCommand]
    private void OpenCatalogFile() => Shell.OpenFile(AppPaths.CatalogFile);

    // ───────────── Kurulum ─────────────

    [RelayCommand]
    private Task InstallAsync() => InstallItemsAsync(Items.Where(i => i.IsSelected && !i.IsInstalled).ToList());

    [RelayCommand]
    private Task RetryFailedAsync() => InstallItemsAsync(Items.Where(i => i.Status == ItemStatus.Failed).ToList());

    private Task InstallItemsAsync(List<CatalogItem> items)
    {
        if (items.Count == 0)
        {
            _app.Dialogs.Info(Loc.Get("Catalog_Title"), Loc.Get("Catalog_NothingSelected"));
            return Task.CompletedTask;
        }

        return _app.Operation.RunAsync(Loc.Get("Catalog_Installing"), async ct =>
        {
            if (!await _app.Winget.IsAvailableAsync(ct))
            {
                if (!_app.Dialogs.Confirm(Loc.Get("Winget_MissingTitle"), Loc.Get("Winget_MissingQuestion"),
                        Loc.Get("Winget_TryInstall"), Loc.Get("Btn_Cancel")))
                    return;
                if (!await _app.Winget.TryInstallAsync(_app.DryRun, ct))
                {
                    _app.Dialogs.Warn(Loc.Get("Winget_MissingTitle"), Loc.Get("Winget_InstallFailed"));
                    return;
                }
            }
            _app.Operation.Report(null, Loc.Get("Restore_CheckInternet"));
            if (!await NetworkService.HasInternetAsync(ct))
            {
                _app.Dialogs.Warn(Loc.Get("Catalog_Title"), Loc.Get("Catalog_NoInternet"));
                return;
            }
            var names = string.Join("\n", items.Take(25).Select(i => "  • " + i.Name)) + (items.Count > 25 ? "\n  …" : "");
            if (!_app.Dialogs.Confirm(Loc.Get("Catalog_Title"), Loc.F("Catalog_InstallConfirm", items.Count, names) +
                                                                (_app.DryRun ? "\n\n" + Loc.Get("Dry_Banner") : "")))
                return;

            foreach (var i in items) { i.Status = ItemStatus.Pending; i.Message = ""; }
            var n = 0;
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                _app.Operation.Report(n++, items.Count, Loc.F("Restore_InstallingApp", item.Name, n, items.Count));
                item.Status = ItemStatus.Running;
                var r = await _app.Winget.InstallAsync(item.Id, item.App.Source, _app.DryRun, ct);
                item.Status = r.Status;
                item.Message = r.Message;
                if (r.Status is ItemStatus.Success or ItemStatus.AlreadyInstalled)
                {
                    if (!_app.DryRun) item.IsInstalled = true;
                    item.IsSelected = false;
                    Log.Success(Loc.F("Restore_AppOkLog", item.Name));
                }
                else if (r.Status == ItemStatus.Failed) Log.Error(Loc.F("Restore_AppFailedLog", item.Name, r.Message));
            }

            var failed = items.Where(i => i.Status == ItemStatus.Failed).ToList();
            HasFailures = failed.Count > 0;
            var ok = items.Count(i => i.Status is ItemStatus.Success or ItemStatus.AlreadyInstalled or ItemStatus.DryRun);
            var lines = new List<string> { Loc.F("Catalog_Result", ok, items.Count) };
            if (failed.Count > 0) lines.Add(Loc.Get("Catalog_FailedList") + "\n" + string.Join("\n", failed.Select(f => $"  • {f.Name}: {f.Message}")));
            if (_app.DryRun) lines.Insert(0, Loc.Get("Dry_Banner"));
            _app.Settings.SetSummary(Loc.Get("Sum_CatalogTitle"), lines);
            _app.Dialogs.Show(failed.Count > 0 ? DialogKind.Warning : DialogKind.Info, Loc.Get("Catalog_Title"),
                string.Join("\n\n", lines), Loc.Get("Btn_Ok"));
        });
    }
}
