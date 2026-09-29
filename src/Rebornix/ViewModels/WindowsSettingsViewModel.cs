using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rebornix.Helpers;
using Rebornix.Services;

namespace Rebornix.ViewModels;

public sealed partial class SettingChoice(SettingDefinition def) : ObservableObject
{
    public SettingDefinition Definition { get; } = def;
    public string Id => Definition.Id;
    public string Title => Definition.Title;
    public string Description => Definition.Description;
    public bool NeedsExplorerRestart => Definition.ExplorerRestart;

    [ObservableProperty] private bool _isSelected = true;
}

public sealed record SnapshotRow(string File, string TimeText, string Items);

public sealed partial class WindowsSettingsViewModel : ObservableObject, IPageActivated
{
    private readonly AppServices _app;

    public WindowsSettingsViewModel(AppServices app)
    {
        _app = app;
        Choices = new ObservableCollection<SettingChoice>(WindowsSettingsService.Definitions.Select(d => new SettingChoice(d)));
        Excluded = WindowsSettingsService.Excluded;
    }

    public ObservableCollection<SettingChoice> Choices { get; }
    public IReadOnlyList<ExcludedSetting> Excluded { get; }
    public ObservableCollection<SnapshotRow> Snapshots { get; } = [];

    /// <summary>Geri alma anlık görüntüleri bu bilgisayarın durumudur; exe'nin yanında tutulur.</summary>
    public static string SnapshotsDir => Path.Combine(AppPaths.BaseDir, "WindowsSettings", "_GeriAlma");

    public List<string> SelectedIds() => Choices.Where(c => c.IsSelected).Select(c => c.Id).ToList();

    public void OnActivated() => RefreshSnapshots();

    [RelayCommand]
    private void RefreshSnapshots()
    {
        Snapshots.Clear();
        foreach (var (file, time, items) in WindowsSettingsService.ListSnapshots(SnapshotsDir))
            Snapshots.Add(new SnapshotRow(file, time.ToString("dd.MM.yyyy HH:mm:ss"), items));
    }

    [RelayCommand]
    private Task UndoAsync(SnapshotRow? row)
    {
        if (row is null) return Task.CompletedTask;
        if (!_app.Dialogs.Confirm(Loc.Get("WinSet_UndoTitle"), Loc.F("WinSet_UndoQuestion", row.TimeText, row.Items), danger: false))
            return Task.CompletedTask;

        return _app.Operation.RunAsync(Loc.Get("WinSet_UndoTitle"), async ct =>
        {
            var report = await Task.Run(() => _app.WindowsSettings.Undo(row.File, SnapshotsDir, _app.DryRun), ct);
            ShowReport(_app, report);
            RefreshSnapshots();
        });
    }

    [RelayCommand]
    private void OpenSnapshotsFolder()
    {
        Directory.CreateDirectory(SnapshotsDir);
        Shell.OpenFolder(SnapshotsDir);
    }

    /// <summary>Uygulama raporunu gösterir; gerekirse Gezgin'i yeniden başlatmayı sorar.</summary>
    public static void ShowReport(AppServices app, ApplyReport report)
    {
        var lines = new List<string>();
        if (report.Applied.Count > 0) lines.Add(Loc.Get("WinSet_ReportApplied") + "\n  • " + string.Join("\n  • ", report.Applied));
        if (report.Skipped.Count > 0) lines.Add(Loc.Get("WinSet_ReportSkipped") + "\n  • " + string.Join("\n  • ", report.Skipped));
        if (report.Failed.Count > 0) lines.Add(Loc.Get("WinSet_ReportFailed") + "\n  • " + string.Join("\n  • ", report.Failed));
        if (report.SnapshotFile is not null) lines.Add(Loc.Get("WinSet_ReportUndoHint"));
        if (app.DryRun) lines.Insert(0, Loc.Get("Dry_Banner"));
        foreach (var l in report.Applied) Log.Success(Loc.F("WinSet_AppliedLog", l));
        foreach (var l in report.Skipped) Log.Warn(l);
        app.Dialogs.Info(Loc.Get("WinSet_ReportTitle"), string.Join("\n\n", lines));

        if (report.ExplorerRestartSuggested && !app.DryRun &&
            app.Dialogs.Confirm(Loc.Get("WinSet_ExplorerTitle"), Loc.Get("WinSet_ExplorerQuestion"),
                Loc.Get("WinSet_ExplorerRestart"), Loc.Get("Btn_Later")))
        {
            WindowsSettingsService.RestartExplorer();
            Log.Info(Loc.Get("WinSet_ExplorerRestarted"));
        }
    }
}

public static class Shell
{
    public static void OpenFolder(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", SafePath.Quote(path))
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn(ex.Message);
        }
    }

    public static void OpenFile(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn(ex.Message);
        }
    }
}
