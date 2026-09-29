using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rebornix.Helpers;
using Rebornix.Models;

namespace Rebornix.Services;

public sealed record LudusaviRunResult(int Games, long Bytes, List<string> Errors);

/// <summary>
/// Ludusavi (https://github.com/mtkennerly/ludusavi) komut satırı sarmalayıcısı.
/// Kullanıcının kendi Ludusavi ayarlarına dokunmamak için Tools\ludusavi-config klasörü kullanılır.
/// </summary>
public sealed partial class LudusaviService
{
    private static readonly HttpClient Http = CreateHttp();

    public static string ExePath => Path.Combine(AppPaths.ToolsDir, "ludusavi.exe");
    public static string ConfigDir => Path.Combine(AppPaths.ToolsDir, "ludusavi-config");
    public static bool IsInstalled => File.Exists(ExePath);

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Rebornix", "1.0"));
        return h;
    }

    private static string Global => $"--config {SafePath.Quote(ConfigDir)} --try-manifest-update";

    /// <summary>GitHub'daki son sürümün win64 paketini indirip Tools klasörüne koyar.</summary>
    public async Task<string> DownloadLatestAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.ToolsDir);
        var json = await Http.GetStringAsync("https://api.github.com/repos/mtkennerly/ludusavi/releases/latest", ct);
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "?";
        var url = doc.RootElement.GetProperty("assets").EnumerateArray()
            .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? ""))
            .FirstOrDefault(a => a.Name.EndsWith("-win64.zip", StringComparison.OrdinalIgnoreCase)).Url;
        if (string.IsNullOrEmpty(url) || !url.StartsWith("https://github.com/mtkennerly/ludusavi/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Loc.Get("Ludusavi_AssetNotFound"));

        Log.Info(Loc.F("Ludusavi_Downloading", tag));
        var zip = Path.Combine(AppPaths.ToolsDir, "ludusavi_download.zip");
        try
        {
            await using (var s = await Http.GetStreamAsync(url, ct))
            await using (var f = File.Create(zip))
                await s.CopyToAsync(f, ct);

            using var archive = ZipFile.OpenRead(zip);
            var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals("ludusavi.exe", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException(Loc.Get("Ludusavi_AssetNotFound"));
            entry.ExtractToFile(ExePath, overwrite: true);
        }
        finally
        {
            try { File.Delete(zip); } catch { /* indirme artığı */ }
        }
        Log.Success(Loc.F("Ludusavi_Installed", tag));
        return tag;
    }

    public async Task<string?> GetVersionAsync(CancellationToken ct)
    {
        if (!IsInstalled) return null;
        var r = await ProcessRunner.RunAsync(ExePath, "--version", ct, Encoding.UTF8);
        return r.Ok ? r.Output.Trim() : null;
    }

    /// <summary>Bu bilgisayardaki save'leri tarar (hiçbir şey kopyalanmaz).</summary>
    public async Task<List<GameSave>> ScanAsync(CancellationToken ct)
    {
        EnsureInstalled();
        var r = await ProcessRunner.RunAsync(ExePath, $"{Global} backup --preview --api", ct, Encoding.UTF8, stdin: "");
        return ParseGames(r.Output, r);
    }

    public async Task<LudusaviRunResult> BackupAsync(IReadOnlyCollection<string> games, string targetDir, bool dryRun,
        CancellationToken ct)
    {
        if (games.Count == 0) return new LudusaviRunResult(0, 0, []);
        EnsureInstalled();
        var args = $"{Global} backup --path {SafePath.Quote(targetDir)} --force --api";
        if (dryRun)
        {
            Log.Info(Loc.F("Dry_LudusaviBackup", games.Count, targetDir));
            args = $"{Global} backup --preview --api";
        }
        else
        {
            Directory.CreateDirectory(targetDir);
        }

        var r = await ProcessRunner.RunAsync(ExePath, args, ct, Encoding.UTF8, stdin: string.Join("\n", games) + "\n");
        var parsed = ParseGames(r.Output, r);
        var errors = CollectErrors(r.Output);
        return new LudusaviRunResult(parsed.Count, parsed.Sum(g => g.Bytes), errors);
    }

    /// <summary>
    /// Geri yükleme önizlemesi. Yedek başka kullanıcı klasöründen alınmışsa (ör. C:\Users\eski → C:\Users\yeni)
    /// Ludusavi'nin "redirect" özelliği ayarlanır.
    /// </summary>
    public async Task<List<GameSave>> RestorePreviewAsync(string backupDir, string? oldUserProfile, CancellationToken ct)
    {
        EnsureInstalled();
        await ConfigureRedirectsAsync(oldUserProfile, ct);
        var r = await ProcessRunner.RunAsync(ExePath,
            $"{Global} restore --path {SafePath.Quote(backupDir)} --preview --api", ct, Encoding.UTF8, stdin: "");
        return ParseGames(r.Output, r);
    }

    public async Task<LudusaviRunResult> RestoreAsync(IReadOnlyCollection<string> games, string backupDir,
        string? oldUserProfile, bool dryRun, CancellationToken ct)
    {
        if (games.Count == 0) return new LudusaviRunResult(0, 0, []);
        EnsureInstalled();
        await ConfigureRedirectsAsync(oldUserProfile, ct);
        var mode = dryRun ? "--preview" : "--force";
        if (dryRun) Log.Info(Loc.F("Dry_LudusaviRestore", games.Count));
        var r = await ProcessRunner.RunAsync(ExePath,
            $"{Global} restore --path {SafePath.Quote(backupDir)} {mode} --api", ct, Encoding.UTF8,
            stdin: string.Join("\n", games) + "\n");
        var parsed = ParseGames(r.Output, r);
        return new LudusaviRunResult(parsed.Count, parsed.Sum(g => g.Bytes), CollectErrors(r.Output));
    }

    private static void EnsureInstalled()
    {
        if (!IsInstalled) throw new InvalidOperationException(Loc.Get("Ludusavi_NotInstalled"));
    }

    private async Task ConfigureRedirectsAsync(string? oldUserProfile, CancellationToken ct)
    {
        var configFile = Path.Combine(ConfigDir, "config.yaml");
        if (!File.Exists(configFile))
        {
            Directory.CreateDirectory(ConfigDir);
            await ProcessRunner.RunAsync(ExePath, $"--config {SafePath.Quote(ConfigDir)} config show", ct, Encoding.UTF8);
        }
        if (!File.Exists(configFile)) return;

        var current = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var redirects = new List<(string, string)>();
        if (!string.IsNullOrWhiteSpace(oldUserProfile) &&
            !string.Equals(SafePath.Normalize(oldUserProfile), SafePath.Normalize(current), StringComparison.OrdinalIgnoreCase))
        {
            redirects.Add((oldUserProfile, current));
            Log.Info(Loc.F("Ludusavi_Redirect", oldUserProfile, current));
        }
        var yaml = await File.ReadAllTextAsync(configFile, ct);
        var updated = ReplaceRedirects(yaml, redirects);
        if (updated != yaml) await File.WriteAllTextAsync(configFile, updated, new UTF8Encoding(false), ct);
    }

    /// <summary>config.yaml içindeki "redirects:" bölümünü yeniden yazar (BOM'suz, girinti korunur).</summary>
    internal static string ReplaceRedirects(string yaml, IReadOnlyList<(string Source, string Target)> redirects)
    {
        static string Y(string p) => "\"" + p.Replace('\\', '/').Replace("\"", "\\\"") + "\"";
        var block = new List<string>();
        if (redirects.Count == 0) block.Add("redirects: []");
        else
        {
            block.Add("redirects:");
            foreach (var (s, t) in redirects)
            {
                block.Add("  - kind: restore");
                block.Add("    source: " + Y(s));
                block.Add("    target: " + Y(t));
            }
        }

        var lines = yaml.Replace("\r\n", "\n").Split('\n').ToList();
        var idx = lines.FindIndex(l => l.StartsWith("redirects:", StringComparison.Ordinal));
        if (idx < 0)
        {
            if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            lines.AddRange(block);
            lines.Add("");
        }
        else
        {
            var end = idx + 1;
            while (end < lines.Count && (lines[end].StartsWith(' ') || lines[end].StartsWith('-'))) end++;
            lines.RemoveRange(idx, end - idx);
            lines.InsertRange(idx, block);
        }
        return string.Join("\n", lines);
    }

    internal static List<GameSave> ParseGames(string output, ProcResult? r = null)
    {
        var start = output.IndexOf('{');
        if (start < 0)
        {
            var detail = r is null ? "" : (r.Error + " " + r.Output).Trim();
            throw new InvalidOperationException(Loc.F("Ludusavi_BadOutput", detail.Length > 300 ? detail[..300] : detail));
        }
        using var doc = JsonDocument.Parse(output[start..]);
        var list = new List<GameSave>();
        if (!doc.RootElement.TryGetProperty("games", out var games)) return list;
        foreach (var g in games.EnumerateObject())
        {
            var save = new GameSave { Name = g.Name };
            if (g.Value.TryGetProperty("change", out var ch)) save.Change = ch.GetString() ?? "";
            if (g.Value.TryGetProperty("files", out var files))
            {
                foreach (var f in files.EnumerateObject())
                {
                    save.FileCount++;
                    if (f.Value.TryGetProperty("bytes", out var b) && b.TryGetInt64(out var bytes)) save.Bytes += bytes;
                    var change = f.Value.TryGetProperty("change", out var c) ? c.GetString() : "";
                    if (change == "Different")
                    {
                        save.ConflictCount++;
                        save.ConflictFiles.Add(f.Name);
                    }
                    else if (change == "New") save.NewCount++;
                }
            }
            if (g.Value.TryGetProperty("registry", out var reg))
                save.RegistryCount = reg.EnumerateObject().Count();
            list.Add(save);
        }
        return list.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static List<string> CollectErrors(string output)
    {
        var errors = new List<string>();
        var start = output.IndexOf('{');
        if (start < 0) return errors;
        try
        {
            using var doc = JsonDocument.Parse(output[start..]);
            if (doc.RootElement.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject()) errors.Add($"{p.Name}: {p.Value}");
            }
            if (doc.RootElement.TryGetProperty("games", out var games))
            {
                foreach (var g in games.EnumerateObject())
                {
                    if (!g.Value.TryGetProperty("files", out var files)) continue;
                    foreach (var f in files.EnumerateObject())
                    {
                        if (f.Value.TryGetProperty("error", out var fe))
                            errors.Add($"{g.Name}: {f.Name} → {fe}");
                    }
                }
            }
        }
        catch (JsonException)
        {
            // yoksay
        }
        return errors;
    }

    /// <summary>Yedek klasöründeki mapping.yaml dosyalarından her oyunun son yedek zamanını okur.</summary>
    public static Dictionary<string, DateTime> ReadBackupTimes(string backupDir)
    {
        var map = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        if (!Directory.Exists(backupDir)) return map;
        foreach (var file in Directory.EnumerateFiles(backupDir, "mapping.yaml", SearchOption.AllDirectories))
        {
            try
            {
                string? name = null;
                DateTime latest = DateTime.MinValue;
                foreach (var line in File.ReadLines(file))
                {
                    if (name is null && line.StartsWith("name:", StringComparison.Ordinal))
                        name = Unquote(line[5..].Trim());
                    var m = WhenRegex().Match(line);
                    // Ludusavi 9 haneli kesir yazar; .NET en fazla 7 hane okur
                    var when = m.Success ? FractionRegex().Replace(m.Groups[1].Value, ".$1") : "";
                    if (m.Success && DateTime.TryParse(when, null,
                            System.Globalization.DateTimeStyles.AdjustToUniversal, out var t) && t > latest)
                        latest = t;
                }
                if (name is not null && latest > DateTime.MinValue) map[name] = latest;
            }
            catch
            {
                // okunamayan mapping dosyası atlanır
            }
        }
        return map;
    }

    /// <summary>"Yeni olanı koru": mevcut çakışan dosyalardan biri yedekten yeniyse true.</summary>
    public static bool LiveIsNewer(GameSave game, DateTime backupUtc)
    {
        foreach (var f in game.ConflictFiles)
        {
            try
            {
                var path = f.Replace('/', '\\');
                if (File.Exists(path) && File.GetLastWriteTimeUtc(path) > backupUtc) return true;
            }
            catch
            {
                // yoksay
            }
        }
        return false;
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            return s[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        if (s.Length >= 2 && s[0] == '\'' && s[^1] == '\'')
            return s[1..^1].Replace("''", "'");
        return s;
    }

    [GeneratedRegex(@"^\s*when:\s*""?([^""]+)""?\s*$")]
    private static partial Regex WhenRegex();

    [GeneratedRegex(@"\.(\d{1,7})\d*")]
    private static partial Regex FractionRegex();
}
