using System.Windows;
using System.Windows.Threading;
using Rebornix.Helpers;
using Rebornix.Services;
using Rebornix.ViewModels;

namespace Rebornix;

public partial class App : Application
{
    private AppServices? _services;
    private string? _smokeDir;
    private string? _langOverride;
    private string? _themeOverride;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Beklenmeyen hatalarda uygulama kapanmasın, kullanıcıya anlaşılır mesaj gösterilsin
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
            Log.Error("Unexpected error: " + (a.ExceptionObject as Exception)?.Message);
        TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            Log.Error("Background error", a.Exception);
            a.SetObserved();
        };

        try
        {
            AppPaths.EnsureBaseFolders();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Get("Err_BaseFolder") + "\n\n" + ex.Message, "Rebornix",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        // Gizli seçenekler (geliştirme / README ekran görüntüleri):
        //   --smoke <klasör>  tüm sayfaları açıp ekran görüntüsü alır ve kapanır
        //   --lang en|tr|de   --theme Dark|Light   (kaydedilmez, sadece bu çalıştırma için)
        _smokeDir = Arg(e.Args, "--smoke");
        _langOverride = Arg(e.Args, "--lang");
        _themeOverride = Arg(e.Args, "--theme");

        _services = new AppServices();
        _services.Settings.Load();
        var s = _services.Settings.Current;
        s.Language = Loc.Normalize(s.Language);
        s.Theme = ThemeManager.Normalize(s.Theme);
        Loc.Apply(_langOverride ?? s.Language);
        ThemeManager.Apply(_themeOverride ?? s.Theme);

        Log.Info(Loc.F("App_Started", typeof(App).Assembly.GetName().Version?.ToString(3), AppPaths.BaseDir));
        if (!Elevation.IsAdmin) Log.Warn(Loc.Get("Home_AdminMissing"));
        if (s.DryRun) Log.Warn(Loc.Get("Dry_Banner"));

        var cleaned = SecureFile.CleanupStaleWifiTemp();
        if (cleaned > 0) Log.Warn(Loc.F("Wifi_StaleTempCleaned", cleaned));

        if (_smokeDir is not null)
        {
            _services.Dialogs.AutoMode = true;
            s.DryRun = true; // test sırasında hiçbir gerçek işlem yapılmaz
        }

        var vm = ShowMainWindow(null);
        if (_smokeDir is not null) _ = SmokeTest.RunAsync((MainWindow)MainWindow, vm, _smokeDir);
        else if (s.ShowWelcome) vm.Welcome.Open();
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && args.Length > i + 1 ? args[i + 1] : null;
    }

    private MainViewModel ShowMainWindow(Window? previous)
    {
        var vm = new MainViewModel(_services!);
        var window = new MainWindow { DataContext = vm };
        if (previous is not null)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = previous.Left;
            window.Top = previous.Top;
            window.Width = previous.Width;
            window.Height = previous.Height;
            window.WindowState = previous.WindowState;
        }
        MainWindow = window;
        window.Show();
        vm.Home.OnActivated();
        return vm;
    }

    /// <summary>
    /// Dil veya tema değişince çağrılır: yeni ayarı uygular ve ana pencereyi yeniden oluşturur
    /// (UAC tekrar sorulmaz). Açık sayfa ve karşılama ekranı durumu korunur.
    /// </summary>
    public static bool ReloadUi(string? pageKey, bool reopenWelcome)
    {
        if (Current is not App app || app._services is null) return false;
        if (app._services.Operation.IsBusy)
        {
            app._services.Dialogs.Warn("Rebornix", Loc.Get("Ui_ReloadBusy"));
            return false;
        }

        var s = app._services.Settings.Current;
        Log.Info($"UI reload: language={s.Language}, theme={s.Theme}, page={pageKey}");
        app._services.Settings.Save();
        app._langOverride = null;
        app._themeOverride = null;
        Loc.Apply(s.Language);
        ThemeManager.Apply(s.Theme);

        var old = app.MainWindow;
        var vm = app.ShowMainWindow(old);
        if (pageKey is not null) vm.Navigate(pageKey);
        if (reopenWelcome) vm.Welcome.Open();
        old?.Close();
        return true;
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Log.Error(Loc.Get("Err_Unexpected"), e.Exception);
        try
        {
            _services?.Dialogs.Error("Rebornix", ErrorText.Friendly(e.Exception));
        }
        catch
        {
            // diyalog gösterilemezse sessizce devam
        }
    }
}
