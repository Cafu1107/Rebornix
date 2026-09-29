using CommunityToolkit.Mvvm.ComponentModel;

namespace Rebornix.Models;

/// <summary>Bellekteki Wi-Fi profili. Şifre (Password) ve Xml asla loglanmaz.</summary>
public sealed class WifiProfile
{
    public string Name { get; set; } = "";
    public string Authentication { get; set; } = "";
    public string? Password { get; set; }
    public string Xml { get; set; } = "";
}

/// <summary>Şifreli yedeğin içindeki içerik (JSON).</summary>
public sealed class WifiPayload
{
    public int Version { get; set; } = 1;
    public DateTime CreatedUtc { get; set; }
    public string ComputerName { get; set; } = "";
    public List<WifiPayloadProfile> Profiles { get; set; } = [];
}

public sealed class WifiPayloadProfile
{
    public string Name { get; set; } = "";
    public string Authentication { get; set; } = "";
    public string Xml { get; set; } = "";
}

public partial class WifiProfileItem : ObservableObject
{
    public WifiProfileItem(WifiProfile profile) => Profile = profile;

    public WifiProfile Profile { get; }
    public string Name => Profile.Name;
    public string Authentication => Profile.Authentication;
    public bool HasPassword => !string.IsNullOrEmpty(Profile.Password);

    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private ItemStatus _status = ItemStatus.Pending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordDisplay))]
    private bool _isPasswordVisible;

    public string PasswordDisplay => !HasPassword ? "—" : IsPasswordVisible ? Profile.Password! : "••••••••••";
}
