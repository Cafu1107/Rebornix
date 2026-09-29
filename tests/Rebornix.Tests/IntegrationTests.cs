using Rebornix.Services;
using Xunit;
using Xunit.Abstractions;

namespace Rebornix.Tests;

/// <summary>
/// Gerçek araçlarla (winget, Ludusavi, Export-WindowsDriver) YALNIZCA OKUMA / DIŞA AKTARMA testleri.
/// Hepsi geçici klasöre yazar; sistemde hiçbir şey kurulmaz veya değiştirilmez.
/// </summary>
[Trait("Category", "Integration")]
[Collection("Ludusavi")]
public class IntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public void Registry_InstalledPrograms_AreRead()
    {
        var all = InstalledAppsService.ReadAll();
        output.WriteLine($"Kayıt defterinde {all.Count} program");
        Assert.NotEmpty(all);
    }

    [Fact]
    public async Task Winget_Export_RealRun()
    {
        var w = new WingetService();
        if (!await w.IsAvailableAsync()) { output.WriteLine("winget yok, atlandı"); return; }
        var file = Path.Combine(TestSetup.NewDir("wexp"), "winget_apps.json");
        var r = await w.ExportAsync(file, CancellationToken.None);
        output.WriteLine($"{r.Packages.Count} paket, {r.UnavailableNames.Count} elle kurulacak: {string.Join(", ", r.UnavailableNames.Take(10))}");
        Assert.NotEmpty(r.Packages);
        var manual = InstalledAppsService.BuildManualList(r.UnavailableNames, InstalledAppsService.ReadAll(), r.Packages.Select(p => p.Id));
        output.WriteLine($"Elle kurulacak (filtreli): {manual.Count}: {string.Join(", ", manual.Select(m => m.Name))}");
        Assert.True(manual.Count <= r.UnavailableNames.Count);

        var installed = await w.GetInstalledIdsAsync(CancellationToken.None);
        Assert.Equal(r.Packages.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase).Count, installed.Count);
    }

    /// <summary>RBX_LUDUSAVI_EXE ortam değişkeni ludusavi.exe'yi gösteriyorsa çalışır.</summary>
    [Fact]
    public async Task Ludusavi_Scan_Backup_RestorePreview()
    {
        var exe = Environment.GetEnvironmentVariable("RBX_LUDUSAVI_EXE");
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) { output.WriteLine("RBX_LUDUSAVI_EXE yok, atlandı"); return; }
        Directory.CreateDirectory(AppPaths.ToolsDir);
        if (!File.Exists(LudusaviService.ExePath)) File.Copy(exe, LudusaviService.ExePath);
        var svc = new LudusaviService();

        var games = await svc.ScanAsync(CancellationToken.None);
        output.WriteLine($"Tarama: {games.Count} oyun");
        Assert.NotEmpty(games); // boş stdin ile TÜM oyunlar taranmalı

        var small = games.Where(g => g.Bytes > 0).OrderBy(g => g.Bytes).First();
        var target = Path.Combine(TestSetup.NewDir("lud"), "Ludusavi");
        var b = await svc.BackupAsync([small.Name], target, dryRun: false, CancellationToken.None);
        output.WriteLine($"Yedek: {small.Name} → {b.Games} oyun, {b.Bytes} bayt");
        Assert.Equal(1, b.Games);
        Assert.True(Directory.EnumerateFiles(target, "mapping.yaml", SearchOption.AllDirectories).Any());

        // Önizleme (dosya yazmaz). Kullanıcı adı değişmiş gibi davran → redirect
        var fakeOld = @"C:\Users\EskiKullanici";
        var preview = await svc.RestorePreviewAsync(target, fakeOld, CancellationToken.None);
        Assert.Single(preview);
        var cfg = File.ReadAllText(Path.Combine(LudusaviService.ConfigDir, "config.yaml"));
        Assert.Contains("C:/Users/EskiKullanici", cfg);

        var preview2 = await svc.RestorePreviewAsync(target, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), CancellationToken.None);
        Assert.Single(preview2);
        Assert.Equal(0, preview2[0].ConflictCount); // az önce yedeklendi, dosyalar aynı olmalı

        var times = LudusaviService.ReadBackupTimes(target);
        Assert.True(times.ContainsKey(small.Name));

        // Deneme modunda geri yükleme = Ludusavi önizleme (dosya yazmaz)
        var r = await svc.RestoreAsync([small.Name], target, null, dryRun: true, CancellationToken.None);
        Assert.Equal(1, r.Games);
    }

    /// <summary>Yönetici olarak çalıştırıldığında gerçek sürücü listeleme ve dışa aktarma (geçici klasöre).</summary>
    [Fact]
    public async Task Drivers_ListAndExport_WhenAdmin()
    {
        if (!Elevation.IsAdmin) { output.WriteLine("Yönetici değil, atlandı"); return; }
        var svc = new DriverService();
        var list = await svc.ListAsync(CancellationToken.None);
        output.WriteLine($"{list.Count} üçüncü parti sürücü");
        Assert.NotEmpty(list);

        // Birkaç küçük sürücü + varsa ağ sürücüsü
        var items = list.Select(d => new Models.DriverItem(d)).ToList();
        var pick = items.Where(i => i.IsCritical).Take(1)
            .Concat(items.Where(i => !i.IsCritical).OrderBy(i => i.Info.SizeBytes).Take(2)).ToList();
        var dir = Path.Combine(TestSetup.NewDir("drvexp"), "Drivers");
        var r = await svc.BackupAsync(pick, dir, dryRun: false, CancellationToken.None);
        output.WriteLine($"Dışa aktarılan: {r.Exported}, {r.TotalBytes} bayt, sorun: {string.Join(" | ", r.Problems)}");
        Assert.Equal(pick.Count, r.Exported);
        Assert.Empty(r.Problems);
        Assert.Empty(Directory.GetDirectories(dir, "_disa_aktarim_*"));
        var manifest = DriverService.LoadManifest(dir);
        Assert.Equal(pick.Count, manifest.Count);
        Assert.Empty(DriverService.Verify(dir, manifest));

        // Geri yükleme yalnızca Deneme modunda doğrulanır
        foreach (var e in DriverService.OrderForInstall(manifest))
        {
            var ir = await svc.InstallAsync(e, dir, dryRun: true, CancellationToken.None);
            Assert.Equal(Models.ItemStatus.DryRun, ir.Status);
            Assert.True(DriverService.HasSameOrNewer(e, list, out _)); // aynı sürüm sistemde var → gerçek kurulumda atlanır
        }
    }

    [Fact]
    public async Task RestorePoint_DryRun_DoesNothing()
    {
        var (ok, _) = await RestorePointService.CreateAsync("test", dryRun: true, CancellationToken.None);
        Assert.True(ok);
    }

    [Fact]
    public async Task Internet_Check_Runs()
    {
        var ok = await NetworkService.HasInternetAsync();
        output.WriteLine("İnternet: " + ok);
    }
}

