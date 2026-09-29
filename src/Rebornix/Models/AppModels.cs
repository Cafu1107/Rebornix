using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Rebornix.Helpers;

namespace Rebornix.Models;

/// <summary>Data\catalog.json dosyası.</summary>
public sealed class CatalogFile
{
    public int Version { get; set; } = 1;
    public List<string> Categories { get; set; } = [];
    public List<CatalogApp> Apps { get; set; } = [];
}

public sealed class CatalogApp
{
    public string Name { get; set; } = "";
    public string Id { get; set; } = "";

    /// <summary>Kategori kimliği (ör. "Browsers"). Görünen ad Strings.resx'teki "Cat_Browsers" metninden gelir.</summary>
    public string Category { get; set; } = "";

    /// <summary>İngilizce açıklama (varsayılan).</summary>
    public string Description { get; set; } = "";

    [JsonPropertyName("description_tr")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DescriptionTr { get; set; }

    [JsonPropertyName("description_de")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DescriptionDe { get; set; }

    public bool Default { get; set; }

    /// <summary>Boşsa "winget". Microsoft Store uygulamaları için "msstore".</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    /// <summary>Etkin arayüz dilindeki açıklama; o dilde yoksa İngilizce.</summary>
    [JsonIgnore]
    public string LocalizedDescription => Loc.Current switch
    {
        "tr" when !string.IsNullOrWhiteSpace(DescriptionTr) => DescriptionTr!,
        "de" when !string.IsNullOrWhiteSpace(DescriptionDe) => DescriptionDe!,
        _ => Description
    };

    /// <summary>Kategori kimliğinin çevrilmiş adı; çevirisi yoksa kimliğin kendisi.</summary>
    public static string CategoryName(string id) => Loc.TryGet("Cat_" + id) ?? id;
}

/// <summary>Kategori filtresindeki bir seçenek. Id boşsa "Tüm kategoriler".</summary>
public sealed record CategoryOption(string Id, string Name);

public partial class CatalogItem : ObservableObject
{
    public CatalogItem(CatalogApp app)
    {
        App = app;
        _isSelected = app.Default;
    }

    public CatalogApp App { get; }
    public string Name => App.Name;
    public string Id => App.Id;
    public string Category => App.Category;
    public string CategoryName => CatalogApp.CategoryName(App.Category);
    public string Description => App.LocalizedDescription;
    public string Initial => string.IsNullOrEmpty(App.Name) ? "?" : App.Name[..1].ToUpperInvariant();

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private ItemStatus _status = ItemStatus.Pending;
    [ObservableProperty] private string _message = "";
}

public sealed class GameSave
{
    public string Name { get; set; } = "";
    public long Bytes { get; set; }
    public int FileCount { get; set; }
    public int RegistryCount { get; set; }
    public int ConflictCount { get; set; }
    public int NewCount { get; set; }
    public List<string> ConflictFiles { get; set; } = [];
    public string Change { get; set; } = "";
}

public partial class GameItem : ObservableObject
{
    public GameItem(GameSave save) => Save = save;

    public GameSave Save { get; }
    public string Name => Save.Name;
    public string SizeText => SafePath.FormatBytes(Save.Bytes);
    public bool HasConflict => Save.ConflictCount > 0;

    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private ItemStatus _status = ItemStatus.Pending;
    [ObservableProperty] private string _message = "";
}

public partial class CustomFolderItem : ObservableObject
{
    public CustomFolderItem(string tokenPath) => TokenPath = tokenPath;

    /// <summary>%USERPROFILE% vb. değişkenli yol.</summary>
    public string TokenPath { get; }
    public string ResolvedPath
    {
        get
        {
            try { return PathTokens.Expand(TokenPath); } catch { return TokenPath; }
        }
    }

    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private ItemStatus _status = ItemStatus.Pending;
}

public sealed class CustomFolderManifest
{
    public List<CustomFolderEntry> Entries { get; set; } = [];
}

public sealed class CustomFolderEntry
{
    public string TokenPath { get; set; } = "";
    public string BackupFolder { get; set; } = "";
    public long Bytes { get; set; }
    public int FileCount { get; set; }
}

public sealed class LudusaviBackupInfo
{
    public string UserProfile { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public List<string> Games { get; set; } = [];
}
