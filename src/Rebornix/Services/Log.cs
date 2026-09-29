using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Rebornix.Models;

namespace Rebornix.Services;

/// <summary>
/// Tüm işlemleri Logs\Rebornix_YYYY-MM-DD.log dosyasına ve arayüzdeki log paneline yazar.
/// Wi-Fi anahtarı gibi hassas veriler için ek bir temizleme katmanı vardır.
/// </summary>
public static partial class Log
{
    private const int MaxUiEntries = 3000;
    private static readonly object FileLock = new();

    public static ObservableCollection<LogEntry> Entries { get; } = [];
    public static event Action<LogEntry>? EntryAdded;

    public static string CurrentFile =>
        Path.Combine(AppPaths.LogsDir, $"Rebornix_{DateTime.Now:yyyy-MM-dd}.log");

    public static void Debug(string msg) => Write(LogLevel.Debug, msg);
    public static void Info(string msg) => Write(LogLevel.Info, msg);
    public static void Success(string msg) => Write(LogLevel.Success, msg);
    public static void Warn(string msg) => Write(LogLevel.Warning, msg);
    public static void Error(string msg) => Write(LogLevel.Error, msg);

    public static void Error(string msg, Exception ex)
    {
        Write(LogLevel.Error, $"{msg}: {ex.Message}");
        // Yığın izi sadece dosyaya
        WriteFile(LogLevel.Debug, ex.ToString());
    }

    public static void Write(LogLevel level, string message)
    {
        var clean = Sanitize(message);
        var entry = new LogEntry(DateTime.Now, level, clean);
        WriteFile(level, clean);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            AddEntry(entry);
        else
            dispatcher.BeginInvoke(() => AddEntry(entry));
    }

    private static void AddEntry(LogEntry entry)
    {
        Entries.Add(entry);
        while (Entries.Count > MaxUiEntries) Entries.RemoveAt(0);
        EntryAdded?.Invoke(entry);
    }

    private static void WriteFile(LogLevel level, string message)
    {
        try
        {
            lock (FileLock)
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                File.AppendAllText(CurrentFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant(),-7}] {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Log yazılamaması uygulamayı durdurmamalı
        }
    }

    /// <summary>Wi-Fi profil XML'indeki anahtar vb. içerik yanlışlıkla gelse bile maskelenir.</summary>
    internal static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message)) return "";
        var s = KeyMaterialRegex().Replace(message, "<keyMaterial>***</keyMaterial>");
        s = KeyContentRegex().Replace(s, "$1***");
        s = PasswordRegex().Replace(s, "$1***");
        return s;
    }

    [GeneratedRegex(@"<keyMaterial>.*?</keyMaterial>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex KeyMaterialRegex();

    // netsh "show profile key=clear" çıktısındaki "Key Content / Anahtar İçeriği : xxx" satırları
    [GeneratedRegex(@"((?:Key Content|Anahtar İçeriği)\s*:\s*)\S.*", RegexOptions.IgnoreCase)]
    private static partial Regex KeyContentRegex();

    [GeneratedRegex(@"((?:password|parola|şifre)\s*[=:]\s*)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordRegex();
}
