using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rebornix.Helpers;

namespace Rebornix.ViewModels;

public sealed record OnboardingPage(string Glyph, string Badge, string Title, string Intro, IReadOnlyList<string> Steps, string Tip)
{
    public bool HasTip => Tip.Length > 0;
}

/// <summary>
/// Açılışta gösterilen "Nasıl kullanılır?" tanıtımı. Metinler Strings.resx içindeki Onb_Pn_* anahtarlarından okunur;
/// adımlar Onb_Pn_Steps içinde satır satır yazılır.
/// </summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    private readonly AppServices _app;
    private readonly Action<string> _navigate;

    public OnboardingViewModel(AppServices app, Action<string> navigate)
    {
        _app = app;
        _navigate = navigate;
        string[] glyphs = ["", "", "", "", "", "", ""];
        Pages = glyphs.Select((g, i) =>
        {
            var n = i + 1;
            var steps = Loc.Get($"Onb_P{n}_Steps").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var tip = Loc.Get($"Onb_P{n}_Tip");
            return new OnboardingPage(g, Loc.Get($"Onb_P{n}_Badge"), Loc.Get($"Onb_P{n}_Title"), Loc.Get($"Onb_P{n}_Intro"),
                steps, tip.StartsWith('[') ? "" : tip);
        }).ToList();
        _current = Pages[0];
    }

    public IReadOnlyList<OnboardingPage> Pages { get; }

    [ObservableProperty] private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(IsFirst), nameof(IsLast), nameof(StepText))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    private int _index;

    [ObservableProperty] private OnboardingPage _current;

    public bool IsFirst => Index == 0;
    public bool IsLast => Index == Pages.Count - 1;
    public string StepText => Loc.F("Onb_StepOf", Index + 1, Pages.Count);

    partial void OnIndexChanged(int value) => Current = Pages[value];

    public void Open()
    {
        Index = 0;
        IsOpen = true;
    }

    [RelayCommand]
    private void Next()
    {
        if (IsLast) Close();
        else Index++;
    }

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back() => Index--;

    private bool CanBack() => Index > 0;

    [RelayCommand]
    private void GoTo(OnboardingPage? page)
    {
        if (page is not null) Index = Pages.ToList().IndexOf(page);
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void Start(string? page)
    {
        Close();
        if (!string.IsNullOrEmpty(page)) _navigate(page);
    }
}
