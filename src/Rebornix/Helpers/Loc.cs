using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace Rebornix.Helpers;

/// <summary>
/// Arayüz metinleri Resources\Strings.resx dosyasından okunur.
/// İngilizce eklemek için Strings.en.resx oluşturmak yeterlidir.
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Rm =
        new("Rebornix.Resources.Strings", typeof(Loc).Assembly);

    public static string Get(string key)
    {
        try
        {
            return Rm.GetString(key, CultureInfo.CurrentUICulture) ?? $"[{key}]";
        }
        catch (MissingManifestResourceException)
        {
            return $"[{key}]";
        }
    }

    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}

/// <summary>XAML içinde kullanım: Text="{h:Loc Nav_Home}"</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension() { }
    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
