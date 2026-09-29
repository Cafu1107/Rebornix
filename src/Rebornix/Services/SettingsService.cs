using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rebornix.Services;

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static T? Read<T>(string file)
    {
        if (!File.Exists(file)) return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(file), Options);
    }

    /// <summary>Önce geçici dosyaya yazar, sonra yerine taşır (yarım kalan dosya oluşmaz).</summary>
    public static void Write<T>(string file, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        File.Move(tmp, file, overwrite: true);
    }
}

public sealed class AppSettings
{
    /// <summary>Deneme modu: gerçek işlem yapmadan ne yapılacağını listeler.</summary>
    public bool DryRun { get; set; }
    public bool OfferRestorePoint { get; set; } = true;

    /// <summary>Açılışta "Nasıl kullanılır?" tanıtımı gösterilsin mi?</summary>
    public bool ShowOnboarding { get; set; } = true;
    public string Language { get; set; } = "tr-TR";
    public string? LastBackupRoot { get; set; }
    public DateTime? LastSummaryTime { get; set; }
    public string LastSummaryTitle { get; set; } = "";
    public List<string> LastSummary { get; set; } = [];

    /// <summary>Kullanıcının eklediği özel save klasörleri (%USERPROFILE% vb. değişkenli).</summary>
    public List<string> CustomFolders { get; set; } = [];
}

public sealed class SettingsService
{
    public AppSettings Current { get; private set; } = new();

    public event Action? Changed;

    public void Load()
    {
        try
        {
            Current = Json.Read<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Warn("Ayar dosyası okunamadı, varsayılanlar kullanılıyor: " + ex.Message);
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Json.Write(AppPaths.SettingsFile, Current);
        }
        catch (Exception ex)
        {
            Log.Warn("Ayarlar kaydedilemedi: " + ex.Message);
        }
        Changed?.Invoke();
    }

    public void SetSummary(string title, IEnumerable<string> lines)
    {
        Current.LastSummaryTitle = title;
        Current.LastSummary = lines.ToList();
        Current.LastSummaryTime = DateTime.Now;
        Save();
    }
}
