using Rebornix.Services;
using Microsoft.Win32;
using Xunit;
using Xunit.Abstractions;

namespace Rebornix.Tests;

/// <summary>
/// Her Windows ayar kalemi AYRI AYRI test edilir: dışa aktar → değiştir → geri yükle → doğrula → geri al → doğrula.
/// Gerçek ayarlara YAZILMAZ: tüm yazmalar HKCU\Software\RebornixTest_* "kum havuzu" anahtarına yönlendirilir.
/// Başlangıç değerleri bu bilgisayarın gerçek ayarlarından (sadece okuyarak) kopyalanır.
/// </summary>
public sealed class WindowsSettingsTests : IDisposable
{
    private readonly string _sandbox = @"Software\RebornixTest_" + Guid.NewGuid().ToString("N")[..8];
    private readonly WindowsSettingsService _real = new();
    private readonly WindowsSettingsService _sb;
    private readonly ITestOutputHelper _out;

    public WindowsSettingsTests(ITestOutputHelper output)
    {
        _out = output;
        _sb = new WindowsSettingsService(_sandbox);
    }

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_sandbox, throwOnMissingSubKey: false);

    /// <summary>Gerçek değerleri (okuyarak) kum havuzuna kopyalar.</summary>
    private int SeedFromReal(SettingDefinition def)
    {
        var refs = def.Kind == SettingKind.Environment
            ? (Registry.CurrentUser.OpenSubKey("Environment")?.GetValueNames() ?? []).Select(n => new RegRef("Environment", n)).ToArray()
            : def.Values.Concat(def.Kind switch
            {
                SettingKind.Regional => new[] { new RegRef(WindowsSettingsService.International, "LocaleName") },
                SettingKind.Wallpaper => new[] { new RegRef(@"Control Panel\Desktop", "WallPaper") },
                _ => Array.Empty<RegRef>()
            }).ToArray();
        var n = 0;
        foreach (var r in refs)
        {
            var v = _real.ReadValue(r.Key, r.Name);
            if (v is null) continue;
            _sb.WriteValue(v);
            n++;
        }
        return n;
    }

    /// <summary>Kum havuzundaki değerleri farklı değerlerle değiştirir (kullanıcının yeni bilgisayarı gibi).</summary>
    private void Mutate(SettingsBackup backup)
    {
        foreach (var item in backup.Items)
        foreach (var v in item.Values)
        {
            if (item.Id == "Environment") continue; // ortam değişkenleri ayrı test ediliyor
            var changed = new RegValueData { Key = v.Key, Name = v.Name, Kind = v.Kind };
            switch (v.Kind)
            {
                case "DWord": changed.Number = (v.Number ?? 0) == 0 ? 1 : 0; break;
                case "QWord": changed.Number = (v.Number ?? 0) + 1; break;
                case "Binary": changed.Base64 = Convert.ToBase64String(new byte[] { 9, 9, 9 }); break;
                case "MultiString": changed.Multi = ["değişti"]; break;
                default: changed.Text = (v.Text ?? "") + "_degisti"; break;
            }
            _sb.WriteValue(changed);
        }
    }

    private void AssertMatches(SettingsBackup expected, string id)
    {
        var item = expected.Items.Single(i => i.Id == id);
        foreach (var v in item.Values)
        {
            var now = _sb.ReadValue(v.Key, v.Name);
            Assert.NotNull(now);
            Assert.True(now!.SameAs(v), $"{id}: {v.Key}\\{v.Name} beklenen değerde değil");
        }
    }

    [Theory]
    [InlineData("Theme")]
    [InlineData("Taskbar")]
    [InlineData("Explorer")]
    [InlineData("MouseKeyboard")]
    [InlineData("Regional")]
    public void RegistryItem_BackupRestoreVerifyUndo(string id)
    {
        var def = WindowsSettingsService.Def(id);
        var seeded = SeedFromReal(def);
        _out.WriteLine($"{id}: bu bilgisayardan {seeded} değer okundu (kum havuzuna kopyalandı)");

        var files = TestSetup.NewDir("ws_" + id);
        var backup = _sb.Capture([id], files);
        var backupFile = Path.Combine(files, "settings.json");
        WindowsSettingsService.Save(backup, backupFile);
        backup = WindowsSettingsService.Load(backupFile)!; // JSON gidiş-dönüşü de test edilsin
        Assert.Single(backup.Items);
        if (id == "Regional") Assert.False(string.IsNullOrEmpty(backup.Items[0].LocaleName));

        // "Yeni bilgisayar": değerler farklı + yedekte olmayan bir değer yok
        Mutate(backup);
        var mutated = _sb.Capture([id], null);

        var snapshots = TestSetup.NewDir("snap_" + id);
        var report = _sb.Apply(backup, [id], files, snapshots, dryRun: false);
        Assert.Empty(report.Failed);
        Assert.Empty(report.Skipped);
        Assert.NotNull(report.SnapshotFile);
        AssertMatches(backup, id);

        // Geri al → değiştirilmiş (yeni bilgisayar) değerlerine dönmeli
        var undo = _sb.Undo(report.SnapshotFile!, snapshots, dryRun: false);
        Assert.Empty(undo.Failed);
        AssertMatches(mutated, id);
    }

    [Fact]
    public void RegistryItem_CreatedValuesAreRemovedOnUndo()
    {
        SeedFromReal(WindowsSettingsService.Def("Explorer"));
        var files = TestSetup.NewDir("ws_created");
        var backup = _sb.Capture(["Explorer"], files);
        Assert.NotEmpty(backup.Items[0].Values);
        var first = backup.Items[0].Values[0];

        // Yeni bilgisayarda bu değer hiç yok
        using (var k = Registry.CurrentUser.OpenSubKey(_sandbox + "\\" + first.Key, true))
            k!.DeleteValue(first.Name);

        var snaps = TestSetup.NewDir("snap_created");
        var report = _sb.Apply(backup, ["Explorer"], files, snaps, false);
        Assert.NotNull(_sb.ReadValue(first.Key, first.Name));

        _sb.Undo(report.SnapshotFile!, snaps, false);
        Assert.Null(_sb.ReadValue(first.Key, first.Name)); // geri almada, sonradan oluşturulan değer silinir
    }

    [Fact]
    public void Regional_DifferentLocale_IsSkipped()
    {
        SeedFromReal(WindowsSettingsService.Def("Regional"));
        var files = TestSetup.NewDir("ws_locale");
        var backup = _sb.Capture(["Regional"], files);
        backup.Items[0].LocaleName = "xx-XX";
        var report = _sb.Apply(backup, ["Regional"], files, TestSetup.NewDir("snap_locale"), false);
        Assert.Single(report.Skipped);
    }

    [Fact]
    public void Environment_MergesPath_AndNeverOverwrites()
    {
        _sb.WriteValue(new RegValueData { Key = "Environment", Name = "Path", Kind = "ExpandString", Text = @"C:\Eski\Bin;%USERPROFILE%\Araclar" });
        _sb.WriteValue(new RegValueData { Key = "Environment", Name = "BENIM_DEGISKEN", Kind = "String", Text = "yedekteki" });
        _sb.WriteValue(new RegValueData { Key = "Environment", Name = "SADECE_YEDEKTE", Kind = "String", Text = "1" });
        var files = TestSetup.NewDir("ws_env");
        var backup = _sb.Capture(["Environment"], files);

        // Yeni bilgisayar: farklı PATH, BENIM_DEGISKEN farklı, SADECE_YEDEKTE yok
        using (var k = Registry.CurrentUser.CreateSubKey(_sandbox + @"\Environment", true))
        {
            k.SetValue("Path", @"C:\Yeni;%USERPROFILE%\Araclar\", RegistryValueKind.ExpandString);
            k.SetValue("BENIM_DEGISKEN", "yeni-bilgisayar");
            k.DeleteValue("SADECE_YEDEKTE");
        }

        var snaps = TestSetup.NewDir("snap_env");
        var report = _sb.Apply(backup, ["Environment"], files, snaps, false);
        Assert.Empty(report.Failed);

        Assert.Equal(@"C:\Yeni;%USERPROFILE%\Araclar\;C:\Eski\Bin", _sb.ReadValue("Environment", "Path")!.Text);
        Assert.Equal("ExpandString", _sb.ReadValue("Environment", "Path")!.Kind);
        Assert.Equal("yeni-bilgisayar", _sb.ReadValue("Environment", "BENIM_DEGISKEN")!.Text); // üzerine yazılmadı
        Assert.Equal("1", _sb.ReadValue("Environment", "SADECE_YEDEKTE")!.Text);             // eksik olan eklendi

        _sb.Undo(report.SnapshotFile!, snaps, false);
        Assert.Equal(@"C:\Yeni;%USERPROFILE%\Araclar\", _sb.ReadValue("Environment", "Path")!.Text);
        Assert.Null(_sb.ReadValue("Environment", "SADECE_YEDEKTE"));
    }

    [Fact]
    public void Wallpaper_FileCopied_AndApplied()
    {
        var picture = Path.Combine(TestSetup.NewDir("wp_src"), "manzara.jpg");
        File.WriteAllBytes(picture, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4]);
        _sb.WriteValue(new RegValueData { Key = @"Control Panel\Desktop", Name = "WallPaper", Kind = "String", Text = picture });
        _sb.WriteValue(new RegValueData { Key = @"Control Panel\Desktop", Name = "WallpaperStyle", Kind = "String", Text = "10" });
        var files = TestSetup.NewDir("ws_wp");
        var backup = _sb.Capture(["Wallpaper"], files);
        Assert.Equal("wallpaper.jpg", backup.Items[0].WallpaperFile);
        Assert.True(File.Exists(Path.Combine(files, "wallpaper.jpg")));

        _sb.WriteValue(new RegValueData { Key = @"Control Panel\Desktop", Name = "WallpaperStyle", Kind = "String", Text = "2" });
        var report = _sb.Apply(backup, ["Wallpaper"], files, TestSetup.NewDir("snap_wp"), false);
        Assert.Empty(report.Failed);
        var applied = _sb.ReadValue(@"Control Panel\Desktop", "WallPaper")!.Text!;
        Assert.True(File.Exists(applied));
        Assert.Equal(File.ReadAllBytes(picture), File.ReadAllBytes(applied));
        Assert.Equal("10", _sb.ReadValue(@"Control Panel\Desktop", "WallpaperStyle")!.Text);
    }

    [Fact]
    public void Apply_DryRun_OnRealRegistry_ChangesNothing()
    {
        // Gerçek kayıt defterinde Deneme modu: hiçbir şey yazılmamalı, anlık görüntü oluşmamalı
        var ids = WindowsSettingsService.Definitions.Select(d => d.Id).Where(i => i != "Wallpaper").ToList();
        var before = _real.Capture(ids, null);
        var fake = _real.Capture(ids, null);
        foreach (var v in fake.Items.SelectMany(i => i.Values).Where(v => v.Kind == "DWord")) v.Number = (v.Number ?? 0) + 1;
        var snaps = TestSetup.NewDir("snap_dry");

        var report = _real.Apply(fake, ids, TestSetup.NewDir("dry_files"), snaps, dryRun: true);

        Assert.Null(report.SnapshotFile);
        Assert.Empty(Directory.GetFileSystemEntries(snaps));
        var after = _real.Capture(ids, null);
        foreach (var item in before.Items)
        {
            var a = after.Items.Single(x => x.Id == item.Id);
            Assert.Equal(item.Values.Count, a.Values.Count);
            for (var i = 0; i < item.Values.Count; i++) Assert.True(item.Values[i].SameAs(a.Values[i]));
        }
    }

    [Fact]
    public void WriteValue_RejectsKeysOutsideWhitelist()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _sb.WriteValue(new RegValueData { Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "x", Kind = "String", Text = "y" }));
    }

    [Fact]
    public void EnvMerge_TranslatesOldProfile_AndSkipsDuplicates()
    {
        var merged = EnvMerge.MergePath(@"C:\A;C:\Users\yeni\bin", @"C:\a\;C:\Users\eski\bin;C:\Users\eski\tools;;C:\B",
            @"C:\Users\eski", @"C:\Users\yeni");
        Assert.Equal(@"C:\A;C:\Users\yeni\bin;C:\Users\yeni\tools;C:\B", merged);
        Assert.Equal(@"C:\X", EnvMerge.MergePath(null, @"C:\X", null, null));
    }
}
