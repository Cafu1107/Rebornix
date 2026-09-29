using System.Runtime.InteropServices;

namespace Rebornix.Helpers;

/// <summary>
/// Kullanıcıya özel yolları %USERPROFILE%, %APPDATA% gibi değişkenlerle saklar.
/// Format sonrası kullanıcı adı değişse bile yol doğru klasöre açılır.
/// </summary>
public static class PathTokens
{
    private static readonly Guid SavedGamesId = new("4C5C32FF-BB9D-43b0-B5B4-2D72E54EAAA4");

    /// <summary>Test ve özel durumlar için klasör çözümleyicisi değiştirilebilir.</summary>
    internal static Func<string, string?> Resolver { get; set; } = DefaultResolve;

    public static readonly string[] Tokens =
    [
        "%LOCALAPPDATA%", "%APPDATA%", "%DOCUMENTS%", "%SAVEDGAMES%",
        "%USERPROFILE%", "%PUBLIC%", "%PROGRAMDATA%"
    ];

    private static string? DefaultResolve(string token) => token switch
    {
        "%LOCALAPPDATA%" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "%APPDATA%" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "%DOCUMENTS%" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "%SAVEDGAMES%" => GetKnownFolder(SavedGamesId),
        "%USERPROFILE%" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "%PUBLIC%" => Environment.GetEnvironmentVariable("PUBLIC"),
        "%PROGRAMDATA%" => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        _ => null
    };

    /// <summary>Tam yolu değişkenli yola çevirir (en uzun eşleşen klasör seçilir).</summary>
    public static string Tokenize(string fullPath)
    {
        var path = SafePath.Normalize(fullPath);
        var best = Tokens
            .Select(t => (Token: t, Dir: Resolver(t)))
            .Where(x => !string.IsNullOrEmpty(x.Dir))
            .Select(x => (x.Token, Dir: SafePath.Normalize(x.Dir!)))
            .Where(x => SafePath.IsUnder(path, x.Dir))
            .OrderByDescending(x => x.Dir.Length)
            .FirstOrDefault();

        if (best.Token is null) return path;
        var rest = path.Length > best.Dir.Length ? path[best.Dir.Length..].TrimStart('\\') : "";
        return rest.Length == 0 ? best.Token : best.Token + "\\" + rest;
    }

    /// <summary>Değişkenli yolu bu bilgisayardaki gerçek yola çevirir.</summary>
    public static string Expand(string tokenPath)
    {
        foreach (var t in Tokens)
        {
            if (!tokenPath.StartsWith(t, StringComparison.OrdinalIgnoreCase)) continue;
            var dir = Resolver(t);
            if (string.IsNullOrEmpty(dir)) break;
            var rest = tokenPath[t.Length..].TrimStart('\\', '/');
            if (rest.Length == 0) return SafePath.Normalize(dir);
            // Değişkenden sonra gelen kısım o klasörün dışına çıkamaz
            return SafePath.Combine(dir, rest);
        }
        return SafePath.Normalize(Environment.ExpandEnvironmentVariables(tokenPath));
    }

    private static string? GetKnownFolder(Guid id)
    {
        try
        {
            if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var p) == 0)
            {
                var s = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
                return s;
            }
        }
        catch
        {
            // yoksay
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);
}
