using Rebornix.Services;

namespace Rebornix.ViewModels;

/// <summary>Uygulama genelinde tek örnek servisler (basit bileşim kökü).</summary>
public sealed class AppServices
{
    public AppServices()
    {
        Settings = new SettingsService();
        Dialogs = new DialogService();
        Operation = new OperationService(Dialogs);
    }

    public SettingsService Settings { get; }
    public DialogService Dialogs { get; }
    public OperationService Operation { get; }
    public WingetService Winget { get; } = new();
    public DriverService Drivers { get; } = new();
    public WifiService Wifi { get; } = new();
    public LudusaviService Ludusavi { get; } = new();
    public CustomFolderService CustomFolders { get; } = new();
    public WindowsSettingsService WindowsSettings { get; } = new();

    public bool DryRun => Settings.Current.DryRun;

    /// <summary>Bu oturumda geri yükleme noktası zaten soruldu/oluşturuldu mu?</summary>
    public bool RestorePointHandled { get; set; }

    /// <summary>
    /// Önemli sistem değişikliğinden önce geri yükleme noktası önerir (oturum başına bir kez).
    /// </summary>
    public async Task OfferRestorePointAsync(CancellationToken ct)
    {
        if (RestorePointHandled || !Settings.Current.OfferRestorePoint) return;
        RestorePointHandled = true;
        if (!Dialogs.Confirm(Helpers.Loc.Get("RestorePoint_Title"), Helpers.Loc.Get("RestorePoint_Question"),
                Helpers.Loc.Get("Btn_Create"), Helpers.Loc.Get("Btn_Skip")))
        {
            Log.Info(Helpers.Loc.Get("RestorePoint_SkippedByUser"));
            return;
        }
        Operation.Report(null, Helpers.Loc.Get("RestorePoint_Creating"));
        var (ok, msg) = await RestorePointService.CreateAsync("Rebornix", DryRun, ct);
        if (ok) Log.Success(msg.Length > 0 ? msg : Helpers.Loc.Get("RestorePoint_Created"));
        else
        {
            Log.Warn(msg);
            if (!Dialogs.Confirm(Helpers.Loc.Get("RestorePoint_Title"), msg + "\n\n" + Helpers.Loc.Get("RestorePoint_ContinueAnyway")))
                throw new OperationCanceledException();
        }
    }
}
