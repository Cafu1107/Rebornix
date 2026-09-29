using System.Text.Json;
using Rebornix.Helpers;
using Rebornix.Models;
using Rebornix.Services;
using Xunit;

namespace Rebornix.Tests;

public class SafePathTests
{
    [Theory]
    [InlineData(@"..\disari")]
    [InlineData(@"a\..\..\disari")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"\\sunucu\paylasim")]
    public void Combine_RejectsTraversal(string rel)
    {
        Assert.Throws<InvalidOperationException>(() => SafePath.Combine(@"D:\Rebornix\Drivers", rel));
    }

    [Fact]
    public void Combine_AllowsNormalRelative() =>
        Assert.Equal(@"D:\Rebornix\Drivers\01_Network\x.inf_amd64", SafePath.Combine(@"D:\Rebornix\Drivers\", @"01_Network\x.inf_amd64"));

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("a/b\\c:d*e?f", "a_b_c_d_e_f")]
    [InlineData("..", "adsiz")]
    [InlineData("  ", "adsiz")]
    public void Sanitize(string input, string expected) => Assert.Equal(expected, SafePath.SanitizeFileName(input));

    [Fact]
    public void IsUnder_DoesNotMatchPrefixSiblings() =>
        Assert.False(SafePath.IsUnder(@"C:\Users\pc2\x", @"C:\Users\pc"));

    [Fact]
    public void Quote_HandlesTrailingBackslash()
    {
        Assert.Equal("\"C:\\a b\\\\\"", SafePath.Quote(@"C:\a b\"));
        Assert.Throws<ArgumentException>(() => SafePath.Quote("a\"b"));
    }
}

public class PathTokenTests
{
    [Fact]
    public void Tokenize_ThenExpand_OnNewUser()
    {
        var old = PathTokens.Resolver;
        try
        {
            PathTokens.Resolver = t => t switch
            {
                "%USERPROFILE%" => @"C:\Users\eski",
                "%APPDATA%" => @"C:\Users\eski\AppData\Roaming",
                "%LOCALAPPDATA%" => @"C:\Users\eski\AppData\Local",
                "%DOCUMENTS%" => @"C:\Users\eski\OneDrive\Belgeler",
                _ => null
            };
            Assert.Equal(@"%APPDATA%\Oyun\Kayitlar", PathTokens.Tokenize(@"C:\Users\eski\AppData\Roaming\Oyun\Kayitlar"));
            Assert.Equal(@"%DOCUMENTS%\My Games", PathTokens.Tokenize(@"C:\Users\eski\OneDrive\Belgeler\My Games"));
            Assert.Equal(@"%USERPROFILE%\Saved Games", PathTokens.Tokenize(@"C:\Users\eski\Saved Games"));
            Assert.Equal(@"D:\Oyunlar", PathTokens.Tokenize(@"D:\Oyunlar"));

            PathTokens.Resolver = t => t switch
            {
                "%USERPROFILE%" => @"C:\Users\yeni",
                "%APPDATA%" => @"C:\Users\yeni\AppData\Roaming",
                "%DOCUMENTS%" => @"C:\Users\yeni\Documents",
                _ => null
            };
            Assert.Equal(@"C:\Users\yeni\AppData\Roaming\Oyun\Kayitlar", PathTokens.Expand(@"%APPDATA%\Oyun\Kayitlar"));
            Assert.Equal(@"C:\Users\yeni\Documents\My Games", PathTokens.Expand(@"%DOCUMENTS%\My Games"));
            Assert.Throws<InvalidOperationException>(() => PathTokens.Expand(@"%APPDATA%\..\..\..\Windows"));
        }
        finally
        {
            PathTokens.Resolver = old;
        }
    }
}

public class CatalogAndWingetTests
{
    [Fact]
    public void EmbeddedCatalog_IsValid()
    {
        var c = JsonSerializer.Deserialize<CatalogFile>(CatalogService.EmbeddedCatalogJson(), Json.Options)!;
        Assert.True(c.Apps.Count >= 40, $"Katalogda {c.Apps.Count} uygulama var");
        Assert.All(c.Apps, a => Assert.True(WingetService.IsValidId(a.Id), a.Id));
        Assert.All(c.Apps, a => Assert.Contains(a.Category, c.Categories));
        Assert.Equal(c.Apps.Count, c.Apps.Select(a => a.Id.ToLowerInvariant()).Distinct().Count());
        Assert.Equal(["Tarayıcılar", "Oyun", "Müzik ve Medya", "İletişim", "Geliştirme", "Araçlar", "Güvenlik", "Ofis"], c.Categories);
        foreach (var id in new[] { "Google.Chrome", "Mozilla.Firefox", "Brave.Brave", "Valve.Steam", "EpicGames.EpicGamesLauncher",
                     "GOG.Galaxy", "Spotify.Spotify", "Discord.Discord", "VideoLAN.VLC", "7zip.7zip", "Notepad++.Notepad++",
                     "Microsoft.VisualStudioCode", "Git.Git", "Python.Python.3.12", "OBSProject.OBSStudio",
                     "Telegram.TelegramDesktop", "Microsoft.PowerToys" })
            Assert.Contains(c.Apps, a => a.Id == id);
    }

    [Fact]
    public void CatalogFile_IsWrittenNextToExe_OnFirstLoad()
    {
        var c = CatalogService.Load();
        Assert.True(File.Exists(AppPaths.CatalogFile));
        Assert.True(c.Apps.Count >= 40);
    }

    [Theory]
    [InlineData("Google.Chrome", true)]
    [InlineData("Notepad++.Notepad++", true)]
    [InlineData("9NKSQGP7F2NH", true)]
    [InlineData("a; rm -rf", false)]
    [InlineData("x\" --override", false)]
    [InlineData("", false)]
    public void IdValidation(string id, bool ok) => Assert.Equal(ok, WingetService.IsValidId(id));

    [Fact]
    public void InstallArgs_AreSilentAndExact() =>
        Assert.Equal("install --id Valve.Steam -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            WingetService.BuildInstallArgs("Valve.Steam", null));

    [Fact]
    public void ParseExport_And_Unavailable()
    {
        var f = Path.Combine(TestSetup.NewDir("winget"), "export.json");
        File.WriteAllText(f, """
            {"$schema":"x","CreationDate":"2026","Sources":[
              {"Packages":[{"PackageIdentifier":"7zip.7zip"},{"PackageIdentifier":"Git.Git"}],"SourceDetails":{"Name":"winget"}},
              {"Packages":[{"PackageIdentifier":"9NKSQGP7F2NH"}],"SourceDetails":{"Name":"msstore"}}]}
            """);
        var p = WingetService.ParseExportFile(f);
        Assert.Equal(3, p.Count);
        Assert.Equal("msstore", p.Single(x => x.Id == "9NKSQGP7F2NH").Source);

        var names = WingetService.ParseUnavailable("""
            Installed package is not available from any source: WinRAR
            Installed version of package is not available from any source: Git
            Installed package is not available from any source: NVIDIA Control Panel
            """);
        Assert.Equal(["NVIDIA Control Panel", "WinRAR"], names);
    }

    [Fact]
    public void ManualList_EnrichedFromRegistry()
    {
        var list = InstalledAppsService.BuildManualList(["WinRAR", "AV1 Video Extension", "Microsoft Visual C++ 2015 x64"],
        [
            new ManualApp { Name = "WinRAR 7.11 (64 bit)", Publisher = "win.rar GmbH", Version = "7.11" },
            new ManualApp { Name = "Microsoft Visual C++ 2015 x64", Publisher = "Microsoft", Version = "14" }
        ]);
        Assert.Single(list); // Store eklentisi (kayıt defterinde yok) ve çalışma zamanı elenir
        Assert.Equal("win.rar GmbH", list[0].Publisher);

        // winget zaten tanıyorsa elle kurulacaklara girmez
        var known = InstalledAppsService.BuildManualList(["Google Chrome", "WinRAR", "Zort"],
        [
            new ManualApp { Name = "Google Chrome" }, new ManualApp { Name = "WinRAR 7.11 (64 bit)" }, new ManualApp { Name = "Zort" }
        ], ["Google.Chrome", "RARLab.WinRAR"]);
        Assert.Equal(["Zort"], known.Select(k => k.Name));
    }

    [Fact]
    public async Task Install_DryRun_DoesNotRunWinget()
    {
        var r = await new WingetService().InstallAsync("Valve.Steam", null, dryRun: true, CancellationToken.None);
        Assert.Equal(ItemStatus.DryRun, r.Status);
    }
}

public class DriverLogicTests
{
    private static DriverManifestEntry E(string inf, string ver, DriverCategory c = DriverCategory.Other, string provider = "Intel") =>
        new() { InfName = inf, Version = ver, Category = c, Provider = provider, DisplayName = inf, RelativePath = inf };

    [Fact]
    public void Category_Mapping()
    {
        Assert.Equal(DriverCategory.Network, DriverCategories.FromClass("Net"));
        Assert.Equal(DriverCategory.Display, DriverCategories.FromClass("Display"));
        Assert.Equal(DriverCategory.Audio, DriverCategories.FromClass("MEDIA"));
        Assert.Equal(DriverCategory.Bluetooth, DriverCategories.FromClass("Bluetooth"));
        Assert.Equal(DriverCategory.Usb, DriverCategories.FromClass("USB"));
        Assert.Equal(DriverCategory.Chipset, DriverCategories.FromClass("System"));
        Assert.Equal(DriverCategory.Other, DriverCategories.FromClass("Camera"));
    }

    [Fact]
    public void Order_NetworkFirst()
    {
        var ordered = DriverService.OrderForInstall([E("ses.inf", "1.0", DriverCategory.Audio), E("wifi.inf", "1.0", DriverCategory.Network), E("gpu.inf", "1.0", DriverCategory.Display)]);
        Assert.Equal("wifi.inf", ordered[0].InfName);
    }

    [Fact]
    public void NewerInstalled_IsSkipped_OlderIsNot()
    {
        var installed = new[] { new DriverInfo { InfName = "netwtw08.inf", Provider = "Intel", Version = "23.40.0.4" } };
        Assert.True(DriverService.HasSameOrNewer(E("netwtw08.inf", "22.100.0.1"), installed, out var v));
        Assert.Equal("23.40.0.4", v);
        Assert.True(DriverService.HasSameOrNewer(E("netwtw08.inf", "23.40.0.4"), installed, out _));
        Assert.False(DriverService.HasSameOrNewer(E("netwtw08.inf", "24.0.0.0"), installed, out _));
        Assert.False(DriverService.HasSameOrNewer(E("netwtw08.inf", "1.0", provider: "Realtek"), installed, out _));
        Assert.False(DriverService.HasSameOrNewer(E("baska.inf", "1.0"), installed, out _));
    }

    [Fact]
    public void Verify_DetectsMissingInfAndEmpty()
    {
        var root = TestSetup.NewDir("drv");
        Directory.CreateDirectory(Path.Combine(root, "ok"));
        File.WriteAllText(Path.Combine(root, "ok", "a.inf"), "[Version]");
        Directory.CreateDirectory(Path.Combine(root, "noinf"));
        File.WriteAllText(Path.Combine(root, "noinf", "a.sys"), "x");
        var problems = DriverService.Verify(root, [E("ok", "1"), E("noinf", "1"), E("yok", "1")]);
        Assert.Equal(2, problems.Count);
        var fallback = DriverService.LoadManifest(root);
        Assert.Single(fallback);
    }

    [Fact]
    public async Task Install_DryRun_DoesNotRunPnputil()
    {
        var r = await new DriverService().InstallAsync(E("x.inf", "1"), TestSetup.NewDir("drv_dry"), dryRun: true, CancellationToken.None);
        Assert.Equal(ItemStatus.DryRun, r.Status);
    }
}

public class CustomFolderTests
{
    [Fact]
    public void CopyTree_Policies()
    {
        var src = TestSetup.NewDir("cf_src");
        var dst = TestSetup.NewDir("cf_dst");
        Directory.CreateDirectory(Path.Combine(src, "alt"));
        File.WriteAllText(Path.Combine(src, "alt", "save1.dat"), "yedek-yeni");
        File.WriteAllText(Path.Combine(src, "save2.dat"), "yedek-eski");
        File.SetLastWriteTimeUtc(Path.Combine(src, "alt", "save1.dat"), DateTime.UtcNow);
        File.SetLastWriteTimeUtc(Path.Combine(src, "save2.dat"), DateTime.UtcNow.AddDays(-5));

        Directory.CreateDirectory(Path.Combine(dst, "alt"));
        File.WriteAllText(Path.Combine(dst, "alt", "save1.dat"), "mevcut-eski");
        File.SetLastWriteTimeUtc(Path.Combine(dst, "alt", "save1.dat"), DateTime.UtcNow.AddDays(-1));
        File.WriteAllText(Path.Combine(dst, "save2.dat"), "mevcut-yeni");

        var s = CustomFolderService.CopyTree(src, dst, ConflictPolicy.KeepNewer, CancellationToken.None);
        Assert.Equal("yedek-yeni", File.ReadAllText(Path.Combine(dst, "alt", "save1.dat")));
        Assert.Equal("mevcut-yeni", File.ReadAllText(Path.Combine(dst, "save2.dat")));
        Assert.Equal(1, s.Skipped);

        CustomFolderService.CopyTree(src, dst, ConflictPolicy.KeepExisting, CancellationToken.None);
        Assert.Equal("mevcut-yeni", File.ReadAllText(Path.Combine(dst, "save2.dat")));

        CustomFolderService.CopyTree(src, dst, ConflictPolicy.UseBackup, CancellationToken.None);
        Assert.Equal("yedek-eski", File.ReadAllText(Path.Combine(dst, "save2.dat")));
    }

    [Fact]
    public void BackupFolderName_IsStableAndSafe()
    {
        var a = CustomFolderService.BackupFolderName(@"%DOCUMENTS%\My Games");
        Assert.Equal(a, CustomFolderService.BackupFolderName(@"%documents%\My Games"));
        Assert.StartsWith("My Games_", a);
        Assert.DoesNotContain("%", CustomFolderService.BackupFolderName(@"%APPDATA%\a:b"));
    }
}

public class LudusaviParsingTests
{
    [Fact]
    public void ParseGames_CountsConflicts()
    {
        var json = """
            {"overall":{"totalGames":2},"games":{
              "Oyun A":{"decision":"Processed","change":"Different","files":{
                  "C:/Users/pc/AppData/a.sav":{"change":"Different","bytes":100},
                  "C:/Users/pc/AppData/b.sav":{"change":"New","bytes":50}},"registry":{}},
              "Oyun B":{"decision":"Processed","change":"Same","files":{"C:/x":{"change":"Same","bytes":7}},
                  "registry":{"HKEY_CURRENT_USER/Software/B":{"change":"Same"}}}}}
            """;
        var games = LudusaviService.ParseGames(json);
        Assert.Equal(2, games.Count);
        var a = games.Single(g => g.Name == "Oyun A");
        Assert.Equal(150, a.Bytes);
        Assert.Equal(1, a.ConflictCount);
        Assert.Equal(1, a.NewCount);
        Assert.Equal(1, games.Single(g => g.Name == "Oyun B").RegistryCount);
    }

    [Fact]
    public void ReplaceRedirects_WritesBlock_AndKeepsRest()
    {
        var yaml = "runtime:\n  threads: ~\nredirects: []\nbackup:\n  path: \"C:/x\"\n";
        var updated = LudusaviService.ReplaceRedirects(yaml, [(@"C:\Users\eski", @"C:\Users\yeni")]);
        Assert.Contains("redirects:\n  - kind: restore\n    source: \"C:/Users/eski\"\n    target: \"C:/Users/yeni\"\nbackup:", updated);

        var back = LudusaviService.ReplaceRedirects(updated, []);
        Assert.Equal(yaml, back);
    }

    [Fact]
    public void ReadBackupTimes_FromMapping()
    {
        var dir = TestSetup.NewDir("lud_map");
        Directory.CreateDirectory(Path.Combine(dir, "Oyun A"));
        File.WriteAllText(Path.Combine(dir, "Oyun A", "mapping.yaml"),
            "---\nname: \"Oyun A\"\ndrives:\n  drive-C: \"C:\"\nbackups:\n  - name: \".\"\n    when: \"2026-09-29T13:35:59.592247200Z\"\n");
        var t = LudusaviService.ReadBackupTimes(dir);
        var when = t["Oyun A"];
        Assert.Equal((2026, 9, 29, 13, 35, 59), (when.Year, when.Month, when.Day, when.Hour, when.Minute, when.Second));
    }
}
