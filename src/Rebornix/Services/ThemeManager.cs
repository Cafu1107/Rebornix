using System.Windows;
using Microsoft.Win32;

namespace Rebornix.Services;

/// <summary>
/// Gece / parlak tema. Renkler Themes/Colors.Dark.xaml veya Colors.Light.xaml'dan, stiller Themes/Styles.xaml'dan gelir.
/// Tema değişince ikisi de yeniden yüklenir; açık pencere App.ReloadUi ile yeniden oluşturulur.
/// </summary>
public static class ThemeManager
{
    public const string Dark = "Dark";
    public const string Light = "Light";
    public const string System = "System";

    public static readonly string[] Supported = [Dark, Light, System];

    /// <summary>Şu an etkin olan tema koyu mu? (Pencerenin başlık çubuğu rengi için.)</summary>
    public static bool IsDark { get; private set; } = true;

    public static string Normalize(string? theme) =>
        Supported.FirstOrDefault(t => string.Equals(t, theme, StringComparison.OrdinalIgnoreCase)) ?? Dark;

    /// <summary>"System" seçiliyse Windows'un uygulama teması (Ayarlar > Kişiselleştirme > Renkler) okunur.</summary>
    public static bool ResolveIsDark(string theme) => Normalize(theme) switch
    {
        Light => false,
        System => !WindowsUsesLightTheme(),
        _ => true
    };

    public static void Apply(string theme)
    {
        IsDark = ResolveIsDark(theme);
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries.Clear();
        // Sıra önemli: Styles.xaml renk anahtarlarını uygulama kaynaklarından okur.
        dictionaries.Add(Load(IsDark ? "Themes/Colors.Dark.xaml" : "Themes/Colors.Light.xaml"));
        dictionaries.Add(Load("Themes/Styles.xaml"));
    }

    private static ResourceDictionary Load(string path) =>
        new() { Source = new Uri($"pack://application:,,,/Rebornix;component/{path}", UriKind.Absolute) };

    private static bool WindowsUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 1;
        }
        catch
        {
            return false;
        }
    }
}
