namespace Rebornix.Helpers;

/// <summary>Yol birleştirme ve dosya adı temizleme; path traversal (..\) saldırılarına karşı koruma.</summary>
public static class SafePath
{
    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    /// <summary>
    /// root ile göreli yolu birleştirir; sonuç root dışına çıkarsa hata verir.
    /// </summary>
    public static string Combine(string root, string relative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        relative ??= "";
        if (Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new InvalidOperationException($"Relative path expected: {relative}");

        var rootFull = Normalize(root);
        var full = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!IsUnder(full, rootFull))
            throw new InvalidOperationException($"Path escapes the target folder: {relative}");
        return full;
    }

    /// <summary>path, root'un kendisi veya altındaysa true.</summary>
    public static bool IsUnder(string path, string root)
    {
        var p = Normalize(path);
        var r = Normalize(root);
        if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) return true;
        var rootWithSep = r.EndsWith(Path.DirectorySeparatorChar) ? r : r + Path.DirectorySeparatorChar;
        return p.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        // "C:\" gibi kök dizinlerde sondaki ayırıcı korunur
        if (full.Length > 3) full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full;
    }

    /// <summary>Herhangi bir metni güvenli bir klasör/dosya adına çevirir.</summary>
    public static string SanitizeFileName(string? name, int maxLength = 80)
    {
        if (string.IsNullOrWhiteSpace(name)) return "unnamed";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) || c < 32 ? '_' : c).ToArray();
        var s = new string(chars).Trim().Trim('.').Trim();
        if (s.Length == 0 || s.All(c => c == '_')) s = "unnamed";
        if (s == "." || s == "..") s = "unnamed";
        var stem = s.Split('.')[0];
        if (ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase)) s = "_" + s;
        if (s.Length > maxLength) s = s[..maxLength].TrimEnd('.', ' ');
        return s;
    }

    /// <summary>Komut satırına verilecek yolu tırnaklar. Windows yollarında " karakteri olamaz.</summary>
    public static string Quote(string path)
    {
        if (path.Contains('"')) throw new ArgumentException("Invalid character in path (\").", nameof(path));
        // Sondaki ters bölü, kapanış tırnağını kaçırmasın
        if (path.EndsWith('\\')) path += "\\";
        return "\"" + path + "\"";
    }

    /// <summary>PowerShell tek tırnaklı literal.</summary>
    public static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : $"{v:0.##} {units[i]}";
    }
}
