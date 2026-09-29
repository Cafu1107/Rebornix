using System.Text;
using Rebornix.Models;
using Rebornix.Services;
using Xunit;

namespace Rebornix.Tests;

/// <summary>Wi-Fi şifreleme testleri: yalnızca SAHTE profiller ve sahte şifreler kullanılır.</summary>
public class WifiTests
{
    private static string FakeProfileXml(string name, string key) => $"""
        <?xml version="1.0"?>
        <WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
          <name>{name}</name>
          <SSIDConfig><SSID><name>{name}</name></SSID></SSIDConfig>
          <connectionType>ESS</connectionType>
          <connectionMode>auto</connectionMode>
          <MSM>
            <security>
              <authEncryption>
                <authentication>WPA2PSK</authentication>
                <encryption>AES</encryption>
                <useOneX>false</useOneX>
              </authEncryption>
              <sharedKey>
                <keyType>passPhrase</keyType>
                <protected>false</protected>
                <keyMaterial>{key}</keyMaterial>
              </sharedKey>
            </security>
          </MSM>
        </WLANProfile>
        """;

    [Fact]
    public void Crypto_RoundTrip_And_Header()
    {
        var plain = Encoding.UTF8.GetBytes("sahte veri 123 — çğüşöı");
        var enc = WifiCrypto.Encrypt(plain, "dogru-parola-1");
        Assert.Equal("RBNIX", Encoding.ASCII.GetString(enc, 0, 5));
        Assert.Equal(600_000, BitConverter.ToInt32(enc, 6));
        Assert.DoesNotContain("sahte", Encoding.UTF8.GetString(enc));
        var dec = WifiCrypto.Decrypt(enc, "dogru-parola-1");
        Assert.Equal(plain, dec);
    }

    [Fact]
    public void Crypto_SaltAndNonce_AreRandom()
    {
        var plain = new byte[] { 1, 2, 3 };
        var a = WifiCrypto.Encrypt(plain, "parola-parola");
        var b = WifiCrypto.Encrypt(plain, "parola-parola");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Crypto_WrongPassword_Throws()
    {
        var enc = WifiCrypto.Encrypt([1, 2, 3], "dogru-parola-1");
        Assert.Throws<WrongPasswordException>(() => WifiCrypto.Decrypt(enc, "yanlis-parola"));
    }

    [Fact]
    public void Crypto_TamperedData_Throws()
    {
        var enc = WifiCrypto.Encrypt(Encoding.UTF8.GetBytes("içerik"), "dogru-parola-1");
        enc[^1] ^= 0xFF; // şifreli veriyi boz
        Assert.Throws<WrongPasswordException>(() => WifiCrypto.Decrypt(enc, "dogru-parola-1"));

        var enc2 = WifiCrypto.Encrypt(Encoding.UTF8.GetBytes("içerik"), "dogru-parola-1");
        enc2[12] ^= 0x01; // başlıktaki salt'ı boz (doğrulanmış ek veri)
        Assert.Throws<WrongPasswordException>(() => WifiCrypto.Decrypt(enc2, "dogru-parola-1"));
    }

    [Fact]
    public void Crypto_NotABackup_Throws()
    {
        Assert.Throws<InvalidBackupFileException>(() => WifiCrypto.Decrypt(Encoding.UTF8.GetBytes("merhaba dünya, bu bir yedek değil....."), "x"));
    }

    [Fact]
    public void ParseXml_ReadsNameAuthAndKey()
    {
        var p = WifiService.ParseXml(FakeProfileXml("SahteAg_5G", "SahteSifre!123"));
        Assert.NotNull(p);
        Assert.Equal("SahteAg_5G", p!.Name);
        Assert.Equal("WPA2PSK", p.Authentication);
        Assert.Equal("SahteSifre!123", p.Password);
    }

    [Fact]
    public async Task Backup_EndToEnd_WithFakeProfiles_EncryptsVerifiesAndWipesTemp()
    {
        var target = Path.Combine(TestSetup.NewDir("wifi"), "WiFi", "wifi.rbxenc");
        var svc = new WifiService
        {
            ExportOverride = (folder, _) =>
            {
                File.WriteAllText(Path.Combine(folder, "Wi-Fi-A.xml"), FakeProfileXml("SahteEv", "sahte-ev-sifresi"));
                File.WriteAllText(Path.Combine(folder, "Wi-Fi-B.xml"), FakeProfileXml("SahteKafe", "sahte-kafe-sifresi"));
                File.WriteAllText(Path.Combine(folder, "Wi-Fi-C.xml"), FakeProfileXml("Istenmeyen", "xxx-yyy-zzz"));
                return Task.CompletedTask;
            }
        };
        var tempBefore = WifiTempDirs();

        var n = await svc.BackupAsync(["SahteEv", "SahteKafe"], "test-parolasi-9", target, dryRun: false, CancellationToken.None);

        Assert.Equal(2, n);
        Assert.True(File.Exists(target));
        Assert.False(File.Exists(target + ".writing"));
        var raw = File.ReadAllText(target, Encoding.Latin1);
        Assert.DoesNotContain("sahte-ev-sifresi", raw);
        Assert.DoesNotContain("SahteEv", raw);

        // Geçici düz metin klasörü silinmiş olmalı
        Assert.Equal(tempBefore, WifiTempDirs());

        var payload = WifiService.OpenBackup(target, "test-parolasi-9");
        Assert.Equal(new[] { "SahteEv", "SahteKafe" }, payload.Profiles.Select(p => p.Name).OrderBy(x => x));
        Assert.Contains("sahte-kafe-sifresi", payload.Profiles.First(p => p.Name == "SahteKafe").Xml);

        Assert.Throws<WrongPasswordException>(() => WifiService.OpenBackup(target, "yanlis"));
    }

    [Fact]
    public async Task Backup_WhenCancelledMidway_TempIsStillWiped()
    {
        var target = Path.Combine(TestSetup.NewDir("wifi"), "wifi.rbxenc");
        using var cts = new CancellationTokenSource();
        var svc = new WifiService
        {
            ExportOverride = (folder, _) =>
            {
                File.WriteAllText(Path.Combine(folder, "a.xml"), FakeProfileXml("SahteAg", "sahte"));
                cts.Cancel();
                return Task.CompletedTask;
            }
        };
        var before = WifiTempDirs();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            svc.BackupAsync(["SahteAg"], "test-parolasi-9", target, false, cts.Token));
        Assert.Equal(before, WifiTempDirs());
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task AddProfiles_DryRun_DoesNothing()
    {
        var statuses = new List<ItemStatus>();
        var n = await new WifiService().AddProfilesAsync(
            [new WifiPayloadProfile { Name = "SahteAg", Xml = FakeProfileXml("SahteAg", "x") }],
            dryRun: true, (_, s, _) => statuses.Add(s), CancellationToken.None);
        Assert.Equal(1, n);
        Assert.Equal([ItemStatus.DryRun], statuses);
    }

    [Fact]
    public void Log_Sanitize_MasksKeys()
    {
        var s = Log.Sanitize("xml: <keyMaterial>GizliSifre123</keyMaterial> ve Key Content : Gizli2 ve password=abc");
        Assert.DoesNotContain("GizliSifre123", s);
        Assert.DoesNotContain("Gizli2", s);
        Assert.DoesNotContain("abc", s);
    }

    [Fact]
    public void SecureWipe_DeletesFile()
    {
        var dir = TestSetup.NewDir("wipe");
        var f = Path.Combine(dir, "x.xml");
        File.WriteAllText(f, "gizli içerik");
        SecureFile.Wipe(f);
        Assert.False(File.Exists(f));
    }

    private static int WifiTempDirs() =>
        Directory.GetDirectories(Path.GetTempPath(), SecureFile.WifiTempPrefix + "*").Length;
}
