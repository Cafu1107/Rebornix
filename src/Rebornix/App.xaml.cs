using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Rebornix.Helpers;
using Rebornix.Services;
using Rebornix.ViewModels;

namespace Rebornix;

public partial class App : Application
{
    private AppServices? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Beklenmeyen hatalarda uygulama kapanmasın, kullanıcıya anlaşılır mesaj gösterilsin
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, a) =>
            Log.Error("Beklenmeyen hata: " + (a.ExceptionObject as Exception)?.Message);
        TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            Log.Error("Arka plan hatası", a.Exception);
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

        _services = new AppServices();
        _services.Settings.Load();
        ApplyLanguage(_services.Settings.Current.Language);

        Log.Info(Loc.F("App_Started", typeof(App).Assembly.GetName().Version?.ToString(3), AppPaths.BaseDir));
        if (!Elevation.IsAdmin) Log.Warn(Loc.Get("Home_AdminMissing"));
        if (_services.Settings.Current.DryRun) Log.Warn(Loc.Get("Dry_Banner"));

        var cleaned = SecureFile.CleanupStaleWifiTemp();
        if (cleaned > 0) Log.Warn(Loc.F("Wifi_StaleTempCleaned", cleaned));

        // Gizli otomatik arayüz testi: tüm sayfaları açıp ekran görüntüsü alır ve kapanır
        var smokeIndex = Array.IndexOf(e.Args, "--smoke");
        var smokeDir = smokeIndex >= 0 && e.Args.Length > smokeIndex + 1 ? e.Args[smokeIndex + 1] : null;
        if (smokeDir is not null)
        {
            _services.Dialogs.AutoMode = true;
            _services.Settings.Current.DryRun = true; // test sırasında hiçbir gerçek işlem yapılmaz
        }

        var vm = new MainViewModel(_services);
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();
        vm.Home.OnActivated();

        if (smokeDir is not null) _ = SmokeTest.RunAsync(window, vm, smokeDir);
        else if (_services.Settings.Current.ShowOnboarding) vm.Onboarding.Open();
    }

    private static void ApplyLanguage(string language)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(language) ? "tr-TR" : language);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // varsayılan dil
        }
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
