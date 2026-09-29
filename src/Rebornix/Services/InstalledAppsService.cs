using System.Text.RegularExpressions;
using Rebornix.Models;
using Microsoft.Win32;

namespace Rebornix.Services;

/// <summary>Kayıt defterindeki Uninstall anahtarlarından (sadece okuma) yüklü programları listeler.</summary>
public static partial class InstalledAppsService
{
    private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<ManualApp> ReadAll()
    {
        var result = new Dictionary<string, ManualApp>(StringComparer.OrdinalIgnoreCase);
        Read(RegistryHive.LocalMachine, RegistryView.Registry64, result);
        Read(RegistryHive.LocalMachine, RegistryView.Registry32, result);
        Read(RegistryHive.CurrentUser, RegistryView.Default, result);
        return result.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void Read(RegistryHive hive, RegistryView view, Dictionary<string, ManualApp> into)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(UninstallPath);
            if (key is null) return;
            foreach (var sub in key.GetSubKeyNames())
            {
                try
                {
                    using var k = key.OpenSubKey(sub);
                    if (k is null) continue;
                    var name = (k.GetValue("DisplayName") as string)?.Trim();
                    if (string.IsNullOrEmpty(name)) continue;
                    if (k.GetValue("SystemComponent") is int sc && sc == 1) continue;
                    if (k.GetValue("ParentKeyName") is string) continue;
                    var releaseType = k.GetValue("ReleaseType") as string ?? "";
                    if (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
                        releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)) continue;
                    if (KbRegex().IsMatch(name)) continue;

                    into.TryAdd(name, new ManualApp
                    {
                        Name = name,
                        Publisher = (k.GetValue("Publisher") as string ?? "").Trim(),
                        Version = (k.GetValue("DisplayVersion") as string ?? "").Trim()
                    });
                }
                catch
                {
                    // okunamayan tek bir anahtar listeyi bozmasın
                }
            }
        }
        catch
        {
            // hive açılamadı
        }
    }

    /// <summary>
    /// winget'in tanımadığı adlardan, "Programlar ve Özellikler"de (kayıt defteri) görünen gerçek programları seçer
    /// ve yayıncı/sürüm bilgisiyle zenginleştirir. Store eklentileri ve sistem bileşenleri (ör. video codec'leri,
    /// çalışma zamanları) elle kurulacak listesine konmaz.
    /// </summary>
    public static List<ManualApp> BuildManualList(IEnumerable<string> unavailableNames, IReadOnlyList<ManualApp> registry,
        IEnumerable<string>? wingetIds = null)
    {
        // winget'in zaten dışa aktardığı paketlerin ad karşılıkları (ör. Google.Chrome → "googlechrome", "chrome")
        var known = new List<string>();
        foreach (var id in wingetIds ?? [])
        {
            var parts = id.Split('.', StringSplitOptions.RemoveEmptyEntries);
            known.Add(Norm(id));
            if (parts.Length > 1)
            {
                var tail = Norm(string.Join("", parts.Skip(1)));
                if (tail.Length >= 4) known.Add(tail);
            }
        }

        var list = new List<ManualApp>();
        foreach (var name in unavailableNames)
        {
            var match = registry.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase))
                        ?? registry.FirstOrDefault(r => r.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));
            if (match is null || NoiseRegex().IsMatch(name)) continue;
            var n = Norm(name);
            if (known.Any(k => n.StartsWith(k, StringComparison.Ordinal))) continue;
            list.Add(new ManualApp { Name = name, Publisher = match.Publisher, Version = match.Version });
        }
        return list;
    }

    private static string Norm(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    [GeneratedRegex(@"^(Microsoft (Visual C\+\+|\.NET|Windows Desktop Runtime|ASP\.NET|Update Health|Edge WebView2|Server Speech|Windows Application Compatibility|GameInput)|Windows (SDK|Software Development Kit|Driver Package)|Kinect for Windows|vs_)|Language Pack",
        RegexOptions.IgnoreCase)]
    private static partial Regex NoiseRegex();

    [GeneratedRegex(@"\(KB\d{6,}\)|^Security Update|^Update for", RegexOptions.IgnoreCase)]
    private static partial Regex KbRegex();
}
