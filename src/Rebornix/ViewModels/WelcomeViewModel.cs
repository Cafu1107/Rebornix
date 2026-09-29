using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Rebornix.ViewModels;

/// <summary>
/// İlk açılışta gösterilen kısa, animasyonlu karşılama ekranı (4 slayt).
/// Ayrıntılı kullanım anlatımı ayrı: <see cref="OnboardingViewModel"/> (rehber).
/// </summary>
public sealed partial class WelcomeViewModel : ObservableObject
{
    public const int SlideCount = 4;
    private readonly AppServices _app;
    private readonly Action _openGuide;

    public WelcomeViewModel(AppServices app, Action openGuide)
    {
        _app = app;
        _openGuide = openGuide;
    }

    public IReadOnlyList<int> Dots { get; } = Enumerable.Range(0, SlideCount).ToList();

    [ObservableProperty] private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLast))]
    private int _index;

    public bool IsLast => Index == SlideCount - 1;

    public void Open()
    {
        Index = 0;
        IsOpen = true;
    }

    [RelayCommand]
    private void Next()
    {
        if (IsLast) Finish();
        else Index++;
    }

    [RelayCommand]
    private void GoTo(int i) => Index = Math.Clamp(i, 0, SlideCount - 1);

    [RelayCommand]
    private void Finish()
    {
        IsOpen = false;
        if (_app.Settings.Current.ShowWelcome)
        {
            _app.Settings.Current.ShowWelcome = false;
            _app.Settings.Save();
        }
    }

    [RelayCommand]
    private void OpenGuide()
    {
        Finish();
        _openGuide();
    }
}
