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

    /// <summary>Anahtar yoksa null döner (ör. kullanıcının katalog.json'a eklediği özel kategori).</summary>
    public static string? TryGet(string key)
    {
        try { return Rm.GetString(key, CultureInfo.CurrentUICulture); }
        catch (MissingManifestResourceException) { return null; }
    }

    public static readonly string[] Supported = ["en", "tr", "de"];

    /// <summary>"tr-TR" gibi eski değerleri de kabul eder; desteklenmeyen dil → "en".</summary>
    public static string Normalize(string? language)
    {
        var two = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant()[..Math.Min(2, language.Trim().Length)];
        return Supported.Contains(two) ? two : "en";
    }

    /// <summary>Etkin arayüz dilinin iki harfli kodu.</summary>
    public static string Current => Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    public static void Apply(string language)
    {
        var culture = CultureInfo.GetCultureInfo(Normalize(language));
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
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
