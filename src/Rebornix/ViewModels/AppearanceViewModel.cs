using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rebornix.Helpers;
using Rebornix.Services;

namespace Rebornix.ViewModels;

public sealed record LanguageOption(string Code, string Name);

public sealed record ThemeOption(string Code, string Name, string Glyph);

/// <summary>
/// Dil ve tema seçimi. Değişince ayar kaydedilir ve pencere App.ReloadUi ile yeniden oluşturulur.
/// Ayarlar sayfası, sol menüdeki güneş/ay düğmesi ve karşılama ekranı bu modeli paylaşır.
/// </summary>
public sealed partial class AppearanceViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly Func<string?> _currentPage;
    private readonly Func<bool> _welcomeOpen;
    private bool _suppress;

    public AppearanceViewModel(AppServices app, Func<string?> currentPage, Func<bool> welcomeOpen)
    {
        _app = app;
        _currentPage = currentPage;
        _welcomeOpen = welcomeOpen;
        Themes =
        [
            new(ThemeManager.Dark, Loc.Get("Theme_Dark"), ""),
            new(ThemeManager.Light, Loc.Get("Theme_Light"), ""),
            new(ThemeManager.System, Loc.Get("Theme_System"), "")
        ];
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == Loc.Normalize(app.Settings.Current.Language)) ?? Languages[0];
        _selectedTheme = Themes.FirstOrDefault(t => t.Code == ThemeManager.Normalize(app.Settings.Current.Theme)) ?? Themes[0];
    }

    /// <summary>Dil adları her dilde kendi yazılışıyla gösterilir.</summary>
    public IReadOnlyList<LanguageOption> Languages { get; } =
        [new("en", "English"), new("tr", "Türkçe"), new("de", "Deutsch")];

    public IReadOnlyList<ThemeOption> Themes { get; }

    [ObservableProperty] private LanguageOption _selectedLanguage;
    [ObservableProperty] private ThemeOption _selectedTheme;

    public bool IsDark => ThemeManager.IsDark;

    /// <summary>Sol menüdeki düğme: koyuysa ay yerine güneş gösterilir (tıklayınca parlak moda geçer).</summary>
    public string ToggleGlyph => IsDark ? "" : "";

    partial void OnSelectedLanguageChanged(LanguageOption? oldValue, LanguageOption newValue)
    {
        if (_suppress || newValue is null || newValue.Code == _app.Settings.Current.Language) return;
        var previous = _app.Settings.Current.Language;
        _app.Settings.Current.Language = newValue.Code;
        if (!App.ReloadUi(_currentPage(), _welcomeOpen()))
        {
            _app.Settings.Current.Language = previous;
            Revert(() => SelectedLanguage = oldValue ?? Languages[0]);
        }
    }

    partial void OnSelectedThemeChanged(ThemeOption? oldValue, ThemeOption newValue)
    {
        if (_suppress || newValue is null || newValue.Code == _app.Settings.Current.Theme) return;
        var previous = _app.Settings.Current.Theme;
        _app.Settings.Current.Theme = newValue.Code;
        if (!App.ReloadUi(_currentPage(), _welcomeOpen()))
        {
            _app.Settings.Current.Theme = previous;
            Revert(() => SelectedTheme = oldValue ?? Themes[0]);
        }
    }

    [RelayCommand]
    private void SelectTheme(ThemeOption? option)
    {
        if (option is not null) SelectedTheme = option;
    }

    [RelayCommand]
    private void ToggleTheme() =>
        SelectedTheme = Themes.First(t => t.Code == (IsDark ? ThemeManager.Light : ThemeManager.Dark));

    private void Revert(Action set)
    {
        _suppress = true;
        set();
        _suppress = false;
    }
}
