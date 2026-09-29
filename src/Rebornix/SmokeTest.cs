using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rebornix.ViewModels;

namespace Rebornix;

/// <summary>
/// "--smoke &lt;klasör&gt;" ile başlatıldığında: Deneme modunda tüm sayfaları sırayla açar, ekran görüntüsü alır,
/// XAML bağlama (binding) hatalarını toplar ve kapanır. Geliştirme/test amaçlıdır; gerçek işlem yapmaz.
/// </summary>
internal static class SmokeTest
{
    private sealed class BindingErrorListener : TraceListener
    {
        public readonly StringBuilder Errors = new();
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            if (message is not null) Errors.AppendLine(message);
        }
    }

    public static async Task RunAsync(Window window, MainViewModel vm, string dir)
    {
        var listener = new BindingErrorListener();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        var errors = new List<string>();
        try
        {
            Directory.CreateDirectory(dir);
            // Belgeler için temiz ekran görüntüsü: log paneli kapalı, yönetici rozeti gizli
            vm.IsLogPanelOpen = false;
            vm.IsAdmin = true;
            await Task.Delay(2500);
            vm.Home.AdminOk = true;
            vm.Home.AdminStatus = Helpers.Loc.Get("Home_AdminOk");

            vm.Welcome.Open();
            for (var i = 0; i < WelcomeViewModel.SlideCount; i++)
            {
                vm.Welcome.Index = i;
                await Settle(window);
                await Task.Delay(1200); // giriş animasyonu bitsin
                Save(window, Path.Combine(dir, $"welcome_{i + 1}.png"));
            }
            vm.Welcome.IsOpen = false;

            vm.Onboarding.Open();
            for (var i = 0; i < vm.Onboarding.Pages.Count; i++)
            {
                vm.Onboarding.Index = i;
                await Settle(window);
                Save(window, Path.Combine(dir, $"guide_{i + 1}.png"));
            }
            vm.Onboarding.IsOpen = false;

            foreach (var nav in vm.NavItems)
            {
                vm.Navigate(nav.Key);
                await Settle(window);
                Save(window, Path.Combine(dir, $"{nav.Key}.png"));
            }

            // En küçük pencere boyutunda düzen kontrolü
            window.WindowState = WindowState.Normal;
            window.Width = window.MinWidth;
            window.Height = window.MinHeight;
            foreach (var key in new[] { "home", "backup", "restore", "catalog" })
            {
                vm.Navigate(key);
                await Settle(window);
                Save(window, Path.Combine(dir, $"min_{key}.png"));
            }
            vm.Onboarding.Open();
            vm.Onboarding.Index = 3;
            await Settle(window);
            Save(window, Path.Combine(dir, "min_guide.png"));
            vm.Onboarding.IsOpen = false;

            // Çalışırken tema ve dil değiştirme: pencere yeniden oluşturulmalı, açık sayfa korunmalı
            for (var i = 0; i < 60 && vm.Operation.IsBusy; i++) await Task.Delay(500);
            window.Width = 1280;
            window.Height = 800;
            vm.Navigate("settings");
            await Settle(window);
            var target = vm.Appearance.Themes.First(t => t.Code != vm.Appearance.SelectedTheme.Code && t.Code != Services.ThemeManager.System);
            vm.Appearance.SelectedTheme = target;
            await Task.Delay(500);
            var reloaded = Application.Current.MainWindow;
            if (ReferenceEquals(reloaded, window)) throw new InvalidOperationException("Theme change did not reload the window");
            var vm2 = (MainViewModel)reloaded.DataContext;
            if (vm2.SelectedNav.Key != "settings") throw new InvalidOperationException("Page was not kept after reload");
            await Settle(reloaded);
            Save(reloaded, Path.Combine(dir, "reload_theme.png"));

            var otherLang = vm2.Appearance.Languages.First(l => l.Code != vm2.Appearance.SelectedLanguage.Code);
            vm2.Appearance.SelectedLanguage = otherLang;
            await Task.Delay(500);
            var reloaded2 = Application.Current.MainWindow;
            if (ReferenceEquals(reloaded2, reloaded)) throw new InvalidOperationException("Language change did not reload the window");
            await Settle(reloaded2);
            Save(reloaded2, Path.Combine(dir, "reload_language.png"));
        }
        catch (Exception ex)
        {
            errors.Add(ex.ToString());
        }

        var report = new StringBuilder();
        report.AppendLine(errors.Count == 0 ? "OK" : "HATA");
        foreach (var e in errors) report.AppendLine(e);
        report.AppendLine("--- Binding errors ---");
        report.Append(listener.Errors);
        await File.WriteAllTextAsync(Path.Combine(dir, "smoke_result.txt"), report.ToString());
        Application.Current.Shutdown(errors.Count == 0 ? 0 : 1);
    }

    private static async Task Settle(Window w)
    {
        await Task.Delay(900);
        await w.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Save(Window w, string file)
    {
        if (w.Content is not FrameworkElement el || el.ActualWidth < 1) return;
        var rtb = new RenderTargetBitmap((int)el.ActualWidth, (int)el.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
