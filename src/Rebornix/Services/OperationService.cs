using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rebornix.Helpers;

namespace Rebornix.Services;

/// <summary>
/// Uzun işlemleri yönetir: tek seferde tek işlem, genel ilerleme çubuğu, iptal, hataları yakalayıp
/// kullanıcıya anlaşılır Türkçe mesaj gösterme (uygulama çökmez).
/// </summary>
public sealed partial class OperationService : ObservableObject
{
    private readonly DialogService _dialogs;
    private CancellationTokenSource? _cts;

    public OperationService(DialogService dialogs) => _dialogs = dialogs;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isIndeterminate;

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        _cts?.Cancel();
        Status = Loc.Get("Op_Cancelling");
    }

    public async Task<bool> RunAsync(string title, Func<CancellationToken, Task> work)
    {
        if (IsBusy)
        {
            _dialogs.Warn(Loc.Get("Op_AlreadyRunningTitle"), Loc.Get("Op_AlreadyRunning"));
            return false;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        Title = title;
        Status = "";
        Progress = 0;
        IsIndeterminate = true;
        Log.Info("▶ " + title);
        try
        {
            await work(_cts.Token);
            Log.Info("■ " + Loc.F("Op_Finished", title));
            return true;
        }
        catch (OperationCanceledException)
        {
            Log.Warn(Loc.F("Op_CancelledLog", title));
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(title, ex);
            _dialogs.Error(title, ErrorText.Friendly(ex));
            return false;
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
            Status = "";
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>İlerleme bildir (herhangi bir iş parçacığından çağrılabilir).</summary>
    public void Report(double? percent, string status)
    {
        void Apply()
        {
            if (percent is { } p)
            {
                IsIndeterminate = false;
                Progress = Math.Clamp(p, 0, 100);
            }
            else IsIndeterminate = true;
            Status = status;
        }

        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) Apply();
        else d.BeginInvoke(Apply);
    }

    public void Report(int done, int total, string status) =>
        Report(total <= 0 ? null : done * 100.0 / total, status);
}

public static class ErrorText
{
    /// <summary>İstisnayı kullanıcıya gösterilecek anlaşılır Türkçe mesaja çevirir.</summary>
    public static string Friendly(Exception ex) => ex switch
    {
        WrongPasswordException => Loc.Get("Wifi_WrongPassword"),
        InvalidBackupFileException => ex.Message,
        WifiUnavailableException => ex.Message,
        UnauthorizedAccessException => Loc.Get("Err_Access") + "\n\n" + ex.Message,
        IOException io when (io.HResult & 0xFFFF) is 0x70 or 0x27 => Loc.Get("Err_DiskFull"),
        IOException => Loc.Get("Err_IO") + "\n\n" + ex.Message,
        Win32Exception { NativeErrorCode: 2 } => Loc.Get("Err_ProgramMissing"),
        Win32Exception { NativeErrorCode: 1223 } => Loc.Get("Err_UserCancelledUac"),
        HttpRequestException or TaskCanceledException => Loc.Get("Err_Network"),
        InvalidOperationException => ex.Message,
        _ => Loc.Get("Err_Unexpected") + "\n\n" + ex.Message
    };
}
