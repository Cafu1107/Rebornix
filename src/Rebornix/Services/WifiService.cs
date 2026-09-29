using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Rebornix.Helpers;
using Rebornix.Models;

namespace Rebornix.Services;

public sealed class WifiUnavailableException(string message) : Exception(message);

/// <summary>
/// Wi-Fi profilleri. GÜVENLİK KURALLARI:
///  - Şifreler ve profil XML'leri hiçbir log'a yazılmaz.
///  - Düz metin XML'ler yalnızca %TEMP%\Rebornix_wifi_* altında durur ve iş biter bitmez sıfırlanıp silinir.
/// </summary>
public sealed class WifiService
{
    private static readonly XNamespace Ns = "http://www.microsoft.com/networking/WLAN/profile/v1";
    private static string Netsh => ProcessRunner.SystemExe("netsh.exe");

    /// <summary>Kayıtlı profilleri (şifreleriyle) belleğe okur; geçici dosyalar hemen silinir.</summary>
    public async Task<List<WifiProfile>> ReadProfilesAsync(CancellationToken ct)
    {
        var tmp = SecureFile.CreateWifiTempDir();
        try
        {
            await ExportToAsync(tmp, ct);
            return ParseFolder(tmp);
        }
        finally
        {
            SecureFile.WipeDirectory(tmp);
        }
    }

    /// <summary>
    /// Yalnızca testler için: netsh yerine sahte profil XML'leri üreten fonksiyon.
    /// (Gerçek Wi-Fi şifrelerine dokunmadan tüm yedekleme akışı test edilebilsin diye.)
    /// </summary>
    internal Func<string, CancellationToken, Task>? ExportOverride { get; set; }

    private async Task ExportToAsync(string folder, CancellationToken ct)
    {
        if (ExportOverride is not null)
        {
            await ExportOverride(folder, ct);
            return;
        }
        // Çıktı loglanmaz; netsh sadece dosya adlarını yazar ama yine de dikkatli olunur.
        var r = await ProcessRunner.RunAsync(Netsh,
            $"wlan export profile key=clear folder={SafePath.Quote(folder)}", ct, logCommand: false);
        Log.Debug("netsh wlan export profile (key=clear) → geçici klasör, çıkış kodu " + r.ExitCode);
        if (!r.Ok)
            throw new WifiUnavailableException(Loc.Get("Wifi_ServiceUnavailable"));
    }

    internal static List<WifiProfile> ParseFolder(string dir)
    {
        var list = new List<WifiProfile>();
        foreach (var f in Directory.EnumerateFiles(dir, "*.xml"))
        {
            var p = ParseXml(File.ReadAllText(f, Encoding.UTF8));
            if (p is not null) list.Add(p);
        }
        return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static WifiProfile? ParseXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null || root.Name.LocalName != "WLANProfile") return null;
            var ns = root.Name.Namespace == XNamespace.None ? Ns : root.Name.Namespace;
            var name = root.Element(ns + "name")?.Value ?? "";
            var security = root.Element(ns + "MSM")?.Element(ns + "security");
            var auth = security?.Element(ns + "authEncryption")?.Element(ns + "authentication")?.Value ?? "";
            var key = security?.Element(ns + "sharedKey")?.Element(ns + "keyMaterial")?.Value;
            return new WifiProfile { Name = name, Authentication = auth, Password = key, Xml = xml };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Seçilen profilleri parolayla şifreleyip tek dosyaya yazar, dosyayı geri açıp doğrular,
    /// sonra düz metin geçici dosyaları güvenli siler (hata/iptal olsa bile).
    /// </summary>
    public async Task<int> BackupAsync(IReadOnlyCollection<string> selectedNames, string password, string targetFile,
        bool dryRun, CancellationToken ct)
    {
        if (selectedNames.Count == 0) return 0;
        if (dryRun)
        {
            Log.Info(Loc.F("Dry_WifiBackup", selectedNames.Count, targetFile));
            return selectedNames.Count;
        }

        var tmp = SecureFile.CreateWifiTempDir();
        var tmpTarget = targetFile + ".yaziliyor";
        byte[]? plain = null;
        try
        {
            await ExportToAsync(tmp, ct);
            ct.ThrowIfCancellationRequested();
            var wanted = new HashSet<string>(selectedNames, StringComparer.Ordinal);
            var profiles = ParseFolder(tmp).Where(p => wanted.Contains(p.Name)).ToList();
            if (profiles.Count == 0) throw new InvalidOperationException(Loc.Get("Wifi_NoSelectedFound"));

            var payload = new WifiPayload
            {
                CreatedUtc = DateTime.UtcNow,
                ComputerName = Environment.MachineName,
                Profiles = profiles.Select(p => new WifiPayloadProfile
                {
                    Name = p.Name, Authentication = p.Authentication, Xml = p.Xml
                }).ToList()
            };
            plain = JsonSerializer.SerializeToUtf8Bytes(payload, Json.Options);
            var encrypted = await Task.Run(() => WifiCrypto.Encrypt(plain, password), ct);

            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            await File.WriteAllBytesAsync(tmpTarget, encrypted, ct);

            // Doğrulama: dosyayı diskten geri oku, çöz, profil sayısını ve XML'leri kontrol et
            var verify = await Task.Run(() => OpenBackup(tmpTarget, password), ct);
            var validXml = verify.Profiles.Count(p => ParseXml(p.Xml) is not null);
            if (verify.Profiles.Count != profiles.Count || validXml != profiles.Count)
                throw new InvalidOperationException(Loc.Get("Wifi_VerifyFailed"));

            File.Move(tmpTarget, targetFile, overwrite: true);
            Log.Success(Loc.F("Wifi_BackupDone", profiles.Count, targetFile));
            return profiles.Count;
        }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            SecureFile.WipeDirectory(tmp);
            try { if (File.Exists(tmpTarget)) File.Delete(tmpTarget); } catch { /* şifreli, zararsız */ }
        }
    }

    /// <summary>Şifreli yedeği bellekte çözer. Yanlış parolada WrongPasswordException.</summary>
    public static WifiPayload OpenBackup(string file, string password)
    {
        var data = File.ReadAllBytes(file);
        var plain = WifiCrypto.Decrypt(data, password);
        try
        {
            return JsonSerializer.Deserialize<WifiPayload>(plain, Json.Options)
                   ?? throw new InvalidBackupFileException("Yedek içeriği boş.");
        }
        catch (JsonException)
        {
            throw new InvalidBackupFileException("Yedek içeriği okunamadı.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>Profilleri tek tek ekler. Her XML geçici klasöre yazılır ve eklenir eklenmez güvenli silinir.</summary>
    public async Task<int> AddProfilesAsync(IReadOnlyList<WifiPayloadProfile> profiles, bool dryRun,
        Action<WifiPayloadProfile, ItemStatus, string>? onStatus, CancellationToken ct)
    {
        var ok = 0;
        if (dryRun)
        {
            foreach (var p in profiles)
            {
                Log.Info(Loc.F("Dry_WifiAdd", p.Name));
                onStatus?.Invoke(p, ItemStatus.DryRun, "");
                ok++;
            }
            return ok;
        }

        var tmp = SecureFile.CreateWifiTempDir();
        try
        {
            var i = 0;
            foreach (var p in profiles)
            {
                ct.ThrowIfCancellationRequested();
                onStatus?.Invoke(p, ItemStatus.Running, "");
                var file = Path.Combine(tmp, $"profil_{i++}.xml");
                try
                {
                    await File.WriteAllTextAsync(file, p.Xml, new UTF8Encoding(false), ct);
                    var r = await ProcessRunner.RunAsync(Netsh,
                        $"wlan add profile filename={SafePath.Quote(file)} user=all", ct, logCommand: false);
                    if (r.Ok)
                    {
                        ok++;
                        Log.Success(Loc.F("Wifi_ProfileAdded", p.Name));
                        onStatus?.Invoke(p, ItemStatus.Success, "");
                    }
                    else
                    {
                        var msg = FirstLine(r.Combined);
                        Log.Error(Loc.F("Wifi_ProfileAddFailed", p.Name, r.ExitCode));
                        onStatus?.Invoke(p, ItemStatus.Failed, msg);
                    }
                }
                finally
                {
                    SecureFile.Wipe(file);
                }
            }
        }
        finally
        {
            SecureFile.WipeDirectory(tmp);
        }
        return ok;
    }

    public async Task<bool> ConnectAsync(string profileName, CancellationToken ct)
    {
        if (profileName.Contains('"')) return false;
        var r = await ProcessRunner.RunAsync(Netsh, $"wlan connect name=\"{profileName}\"", ct);
        return r.Ok;
    }

    private static string FirstLine(string s) =>
        s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
}

public static class NetworkService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    /// <summary>Windows'un kendi bağlantı testi adresiyle internet kontrolü.</summary>
    public static async Task<bool> HasInternetAsync(CancellationToken ct = default)
    {
        if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()) return false;
        try
        {
            var s = await Http.GetStringAsync("http://www.msftconnecttest.com/connecttest.txt", ct);
            if (s.Contains("Microsoft Connect Test", StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // ikinci deneme aşağıda
        }
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, "https://www.microsoft.com");
            using var res = await Http.SendAsync(req, ct);
            return (int)res.StatusCode < 500;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }
}
