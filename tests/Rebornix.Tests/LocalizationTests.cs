using System.Globalization;
using System.Resources;
using System.Xml.Linq;
using Rebornix.Helpers;
using Rebornix.Models;
using Rebornix.Services;
using Xunit;

namespace Rebornix.Tests;

/// <summary>Dil (en/tr/de) ve tema (koyu/açık) tutarlılık testleri.</summary>
public class LocalizationTests
{
    private static readonly ResourceManager Rm = new("Rebornix.Resources.Strings", typeof(Loc).Assembly);

    private static Dictionary<string, string> Strings(string culture)
    {
        var set = Rm.GetResourceSet(CultureInfo.GetCultureInfo(culture), createIfNotExists: true, tryParents: false)!;
        return set.Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    [Theory]
    [InlineData("tr")]
    [InlineData("de")]
    public void EveryLanguage_HasAllEnglishKeys_AndSamePlaceholders(string culture)
    {
        var en = Strings(CultureInfo.InvariantCulture.Name);
        var other = Strings(culture);
        Assert.True(en.Count > 500, $"English has only {en.Count} keys");
        Assert.Empty(en.Keys.Except(other.Keys));
        Assert.Empty(other.Keys.Except(en.Keys));
        foreach (var (key, value) in en)
        {
            static string P(string s) => string.Join(",", System.Text.RegularExpressions.Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Distinct().Order());
            Assert.True(P(value) == P(other[key]), $"{culture}: placeholders differ in {key}");
        }
    }

    [Fact]
    public void DefaultLanguage_IsEnglish()
    {
        Assert.Equal("en", new AppSettings().Language);
        Assert.Equal("Back up", Strings(CultureInfo.InvariantCulture.Name)["Nav_Backup"]);
        Assert.Equal("Yedekle", Strings("tr")["Nav_Backup"]);
        Assert.Equal("Sichern", Strings("de")["Nav_Backup"]);
    }

    [Theory]
    [InlineData(null, "en")]
    [InlineData("tr-TR", "tr")]
    [InlineData("DE", "de")]
    [InlineData("fr", "en")]
    public void Language_Normalize(string? input, string expected) => Assert.Equal(expected, Loc.Normalize(input));

    [Fact]
    public void CatalogDescriptions_FollowUiLanguage()
    {
        var app = new CatalogApp { Description = "Browser", DescriptionTr = "Tarayıcı", DescriptionDe = "Browser DE" };
        var saved = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr");
            Assert.Equal("Tarayıcı", app.LocalizedDescription);
            Assert.Equal("Tarayıcılar", CatalogApp.CategoryName("Browsers"));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
            Assert.Equal("Browser DE", app.LocalizedDescription);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            Assert.Equal("Browser", app.LocalizedDescription);
            Assert.Equal("Browsers", CatalogApp.CategoryName("Browsers"));
            Assert.Equal("MyOwnCategory", CatalogApp.CategoryName("MyOwnCategory")); // kullanıcı kategorisi
        }
        finally
        {
            CultureInfo.CurrentUICulture = saved;
        }
    }

    [Fact]
    public void DarkAndLightThemes_DefineTheSameKeys()
    {
        static HashSet<string> Keys(string file)
        {
            var path = Path.Combine(FindRepoRoot(), "src", "Rebornix", "Themes", file);
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            return XDocument.Load(path).Root!.Elements().Select(e => (string?)e.Attribute(x + "Key")).OfType<string>().ToHashSet();
        }
        var dark = Keys("Colors.Dark.xaml");
        var light = Keys("Colors.Light.xaml");
        Assert.True(dark.Count > 30);
        Assert.Empty(dark.Except(light));
        Assert.Empty(light.Except(dark));
    }

    [Theory]
    [InlineData(null, "Dark")]
    [InlineData("light", "Light")]
    [InlineData("SYSTEM", "System")]
    [InlineData("blue", "Dark")]
    public void Theme_Normalize(string? input, string expected) => Assert.Equal(expected, ThemeManager.Normalize(input));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rebornix.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Rebornix.sln not found");
    }
}
