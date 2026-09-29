using Rebornix.Models;
using Rebornix.Services;
using Rebornix.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Rebornix.Tests;

/// <summary>
/// Uçtan uca: uygulamanın kendi ekran modelleriyle geçici klasöre GERÇEK yedek alınır,
/// ardından aynı yedekten DENEME MODUNDA tüm geri yükleme adımları sırayla çalıştırılır.
/// Sistemde hiçbir şey kurulmaz/değiştirilmez.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Ludusavi")]
public class EndToEndTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Backup_Real_Then_RestoreAll_DryRun()
    {
        var ludusavi = Environment.GetEnvironmentVariable("RBX_LUDUSAVI_EXE");
        if (string.IsNullOrEmpty(ludusavi) || !File.Exists(ludusavi)) { output.WriteLine("RBX_LUDUSAVI_EXE yok, atlandı"); return; }
        Directory.CreateDirectory(AppPaths.ToolsDir);
        if (!File.Exists(LudusaviService.ExePath)) File.Copy(ludusavi, LudusaviService.ExePath);

        var app = new AppServices();
        app.Dialogs.AutoMode = true;
        app.Dialogs.AutoConfirm = true;
        app.Settings.Current.DryRun = false;
        var main = new MainViewModel(app);

        // ───── Yedekleme (gerçek, geçici klasöre) ─────
        var root = Path.Combine(TestSetup.NewDir("e2e"), "Rebornix");
        var b = main.Backup;
        b.BackupRoot = root;
        b.IncludeDrivers = Elevation.IsAdmin;
        b.IncludeApps = true;
        b.IncludeSaves = true;
        b.IncludeSettings = true;

        await b.ScanGamesCommand.ExecuteAsync(null);
        Assert.NotEmpty(b.Games);
        var smallest = b.Games.Where(g => g.Save.Bytes > 0).OrderBy(g => g.Save.Bytes).First();
        foreach (var g in b.Games) g.IsSelected = g == smallest;

        var custom = TestSetup.NewDir("e2e_custom");
        File.WriteAllText(Path.Combine(custom, "ayar.cfg"), "sahte oyun ayarı");
        b.CustomFolders.Clear();
        b.CustomFolders.Add(new CustomFolderItem(custom));

        await b.StartBackupCommand.ExecuteAsync(null);

        var layout = new BackupLayout(root);
        var manifest = Json.Read<BackupManifest>(layout.ManifestFile);
        Assert.NotNull(manifest);
        output.WriteLine("Yedek özeti:\n" + string.Join("\n", b.SummaryLines));
        Assert.Empty(manifest!.Errors);
        Assert.True(File.Exists(layout.AppsFile));
        Assert.True(File.Exists(layout.ManualAppsFile));
        Assert.True(File.Exists(layout.WindowsSettingsFile));
        Assert.True(File.Exists(layout.CustomManifest));
        Assert.True(File.Exists(layout.LudusaviInfoFile));
        Assert.True(Directory.EnumerateFiles(layout.LudusaviDir, "mapping.yaml", SearchOption.AllDirectories).Any());
        Assert.Equal(1, manifest.GameCount);
        Assert.Equal(7, manifest.SettingsItems.Count);
        Assert.True(manifest.AppCount > 0);

        // ───── Geri yükleme (DENEME MODU) ─────
        app.Settings.Current.DryRun = true;
        var r = main.Restore;
        r.SelectedCandidate = new BackupCandidate(layout.Root, manifest);
        Assert.True(r.HasApps);
        Assert.True(r.HasSaves);
        Assert.True(r.HasSettings);
        Assert.False(r.HasWifi);

        var settingsBefore = new WindowsSettingsService().Capture(WindowsSettingsService.Definitions.Select(d => d.Id).Where(i => i != "Wallpaper"), null);

        await r.RunAllCommand.ExecuteAsync(null);

        output.WriteLine($"Uygulama satırları: {r.AppItems.Count}, durumlar: " +
                         string.Join(", ", r.AppItems.GroupBy(a => a.Status).Select(g => $"{g.Key}={g.Count()}")));
        Assert.All(r.AppItems, a => Assert.Contains(a.Status, new[] { ItemStatus.DryRun, ItemStatus.AlreadyInstalled }));
        Assert.All(r.GameItems.Where(g => g.IsSelected), g => Assert.Equal(ItemStatus.DryRun, g.Status));
        Assert.All(r.CustomItems, c => Assert.Equal(ItemStatus.DryRun, c.Status));
        Assert.All(r.SettingItems, s => Assert.Contains(s.Status, new[] { ItemStatus.DryRun, ItemStatus.Skipped }));
        Assert.Empty(r.FailedLines);
        Assert.False(File.Exists(layout.ProgressFile)); // deneme modunda ilerleme dosyası yazılmaz

        // Gerçek ayarlar değişmedi
        var settingsAfter = new WindowsSettingsService().Capture(settingsBefore.Items.Select(i => i.Id), null);
        foreach (var item in settingsBefore.Items)
        {
            var after = settingsAfter.Items.Single(x => x.Id == item.Id);
            Assert.Equal(item.Values.Count, after.Values.Count);
            for (var i = 0; i < item.Values.Count; i++) Assert.True(item.Values[i].SameAs(after.Values[i]));
        }
    }
}

