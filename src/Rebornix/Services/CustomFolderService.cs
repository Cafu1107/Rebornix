using System.Security.Cryptography;
using System.Text;
using Rebornix.Helpers;
using Rebornix.Models;

namespace Rebornix.Services;

public sealed record CopyStats(int Files, long Bytes, int Skipped, int Errors);

/// <summary>Kullanıcının elle eklediği klasörleri yedekler / geri yükler.</summary>
public sealed class CustomFolderService
{
    /// <summary>Değişkenli yoldan kararlı, güvenli bir yedek klasör adı üretir.</summary>
    public static string BackupFolderName(string tokenPath)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenPath.ToUpperInvariant())))[..8];
        var last = Path.GetFileName(tokenPath.TrimEnd('\\'));
        return SafePath.SanitizeFileName(last, 50) + "_" + hash;
    }

    public async Task<CustomFolderManifest> BackupAsync(IReadOnlyList<string> tokenPaths, string customDir, bool dryRun,
        Action<string, ItemStatus>? onStatus, CancellationToken ct)
    {
        var manifest = new CustomFolderManifest();
        foreach (var token in tokenPaths)
        {
            ct.ThrowIfCancellationRequested();
            onStatus?.Invoke(token, ItemStatus.Running);
            string source;
            try { source = PathTokens.Expand(token); }
            catch (Exception ex)
            {
                Log.Error(Loc.F("Custom_BadPath", token), ex);
                onStatus?.Invoke(token, ItemStatus.Failed);
                continue;
            }
            if (!Directory.Exists(source))
            {
                Log.Warn(Loc.F("Custom_SourceMissing", source));
                onStatus?.Invoke(token, ItemStatus.Skipped);
                continue;
            }

            var folder = BackupFolderName(token);
            var dest = SafePath.Combine(customDir, folder);
            if (dryRun)
            {
                Log.Info(Loc.F("Dry_CustomCopy", source, dest));
                onStatus?.Invoke(token, ItemStatus.DryRun);
                manifest.Entries.Add(new CustomFolderEntry { TokenPath = token, BackupFolder = folder });
                continue;
            }

            var stats = await Task.Run(() => CopyTree(source, dest, ConflictPolicy.UseBackup, ct), ct);
            manifest.Entries.Add(new CustomFolderEntry
            {
                TokenPath = token, BackupFolder = folder, Bytes = stats.Bytes, FileCount = stats.Files
            });
            Log.Success(Loc.F("Custom_Copied", token, stats.Files, SafePath.FormatBytes(stats.Bytes)));
            onStatus?.Invoke(token, stats.Errors > 0 ? ItemStatus.Failed : ItemStatus.Success);
        }
        if (!dryRun) Json.Write(Path.Combine(customDir, "custom_folders.json"), manifest);
        return manifest;
    }

    public async Task<CopyStats> RestoreAsync(CustomFolderEntry entry, string customDir, ConflictPolicy policy,
        bool dryRun, CancellationToken ct)
    {
        var src = SafePath.Combine(customDir, SafePath.SanitizeFileName(entry.BackupFolder, 120));
        var dest = PathTokens.Expand(entry.TokenPath);
        if (dryRun)
        {
            Log.Info(Loc.F("Dry_CustomRestore", src, dest, Loc.Get("Policy_" + policy)));
            return new CopyStats(0, 0, 0, 0);
        }
        return await Task.Run(() => CopyTree(src, dest, policy, ct), ct);
    }

    /// <summary>
    /// Klasör ağacını kopyalar. Hedefte dosya varsa politikaya göre:
    /// KeepNewer: kaynak daha yeniyse yazar; UseBackup: her zaman yazar; KeepExisting: dokunmaz.
    /// Sembolik bağlantılar izlenmez.
    /// </summary>
    public static CopyStats CopyTree(string source, string dest, ConflictPolicy policy, CancellationToken ct)
    {
        int files = 0, skipped = 0, errors = 0;
        long bytes = 0;
        var srcRoot = SafePath.Normalize(source);
        var destRoot = SafePath.Normalize(dest);
        Directory.CreateDirectory(destRoot);

        var stack = new Stack<string>();
        stack.Push(srcRoot);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(dir).ToList(); }
            catch (Exception ex)
            {
                errors++;
                Log.Warn(Loc.F("Custom_ReadError", dir, ex.Message));
                continue;
            }

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var attr = File.GetAttributes(entry);
                    if (attr.HasFlag(FileAttributes.ReparsePoint)) { skipped++; continue; }
                    var rel = Path.GetRelativePath(srcRoot, entry);
                    var target = SafePath.Combine(destRoot, rel);
                    if (attr.HasFlag(FileAttributes.Directory))
                    {
                        Directory.CreateDirectory(target);
                        stack.Push(entry);
                        continue;
                    }

                    if (File.Exists(target))
                    {
                        var write = policy switch
                        {
                            ConflictPolicy.UseBackup => true,
                            ConflictPolicy.KeepExisting => false,
                            _ => File.GetLastWriteTimeUtc(entry) > File.GetLastWriteTimeUtc(target)
                        };
                        if (!write) { skipped++; continue; }
                        File.SetAttributes(target, FileAttributes.Normal);
                    }
                    File.Copy(entry, target, overwrite: true);
                    File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(entry));
                    files++;
                    bytes += new FileInfo(entry).Length;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors++;
                    Log.Warn(Loc.F("Custom_CopyError", entry, ex.Message));
                }
            }
        }
        return new CopyStats(files, bytes, skipped, errors);
    }
}
