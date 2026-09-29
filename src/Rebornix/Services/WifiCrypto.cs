using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Rebornix.Services;

public sealed class WrongPasswordException() : Exception("Parola yanlış veya dosya bozuk.");

public sealed class InvalidBackupFileException(string message) : Exception(message);

/// <summary>
/// .rbxenc dosya biçimi:
///   "RBNIX" (5 bayt) | sürüm (1) | PBKDF2 tur sayısı (int32 LE) | salt (16) | nonce (12) | tag (16) | şifreli veri
/// Anahtar: PBKDF2-HMAC-SHA256(parola, salt, tur) → 32 bayt (AES-256).
/// Başlık (ilk 38 bayt) GCM "ek doğrulanmış veri" olarak kullanılır; başlık değiştirilirse çözme başarısız olur.
/// Parola hiçbir yere yazılmaz.
/// </summary>
public static class WifiCrypto
{
    public const int Iterations = 600_000;
    private const byte FormatVersion = 1;
    private static readonly byte[] Magic = "RBNIX"u8.ToArray();
    private const int SaltSize = 16, NonceSize = 12, TagSize = 16;
    private const int HeaderSize = 5 + 1 + 4 + SaltSize + NonceSize; // 38
    public const int MinPasswordLength = 8;

    public static byte[] Encrypt(byte[] plaintext, string password, int iterations = Iterations)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Parola boş olamaz.", nameof(password));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);

        var output = new byte[HeaderSize + TagSize + plaintext.Length];
        var span = output.AsSpan();
        Magic.CopyTo(span);
        span[5] = FormatVersion;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(6, 4), iterations);
        salt.CopyTo(span.Slice(10, SaltSize));
        nonce.CopyTo(span.Slice(10 + SaltSize, NonceSize));

        var key = DeriveKey(password, salt, iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext,
                span.Slice(HeaderSize + TagSize, plaintext.Length),
                span.Slice(HeaderSize, TagSize),
                span[..HeaderSize]);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
        return output;
    }

    public static byte[] Decrypt(byte[] data, string password)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < HeaderSize + TagSize || !data.AsSpan(0, 5).SequenceEqual(Magic))
            throw new InvalidBackupFileException("Bu dosya bir Rebornix Wi-Fi yedeği değil.");
        if (data[5] != FormatVersion)
            throw new InvalidBackupFileException($"Desteklenmeyen yedek sürümü: {data[5]}");

        var span = data.AsSpan();
        var iterations = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(6, 4));
        if (iterations is < 100_000 or > 50_000_000)
            throw new InvalidBackupFileException("Yedek başlığı geçersiz.");
        var salt = span.Slice(10, SaltSize).ToArray();
        var nonce = span.Slice(10 + SaltSize, NonceSize);
        var tag = span.Slice(HeaderSize, TagSize);
        var cipher = span[(HeaderSize + TagSize)..];

        var plain = new byte[cipher.Length];
        var key = DeriveKey(password, salt, iterations);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain, span[..HeaderSize]);
            return plain;
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(plain);
            throw new WrongPasswordException();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations)
    {
        var pwBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(pwBytes, salt, iterations, HashAlgorithmName.SHA256, 32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pwBytes);
        }
    }
}

public static class SecureFile
{
    /// <summary>Dosyanın içeriğini sıfırlarla ezer, diske yazdırır ve siler.</summary>
    public static void Wipe(string file)
    {
        if (!File.Exists(file)) return;
        try
        {
            File.SetAttributes(file, FileAttributes.Normal);
            var length = new FileInfo(file).Length;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                var zeros = new byte[64 * 1024];
                long written = 0;
                while (written < length)
                {
                    var n = (int)Math.Min(zeros.Length, length - written);
                    fs.Write(zeros, 0, n);
                    written += n;
                }
                fs.Flush(flushToDisk: true);
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Klasördeki tüm dosyaları güvenli siler, sonra klasörü kaldırır.</summary>
    public static void WipeDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { Wipe(f); }
            catch (Exception ex) { Log.Warn($"Geçici dosya silinemedi ({Path.GetFileName(f)}): {ex.Message}"); }
        }
        try { Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { Log.Warn("Geçici klasör silinemedi: " + ex.Message); }
    }

    public const string WifiTempPrefix = "Rebornix_wifi_";

    public static string CreateWifiTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), WifiTempPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Önceki çalıştırmada (çökme/kapanma) kalmış düz metin Wi-Fi geçici klasörlerini temizler.</summary>
    public static int CleanupStaleWifiTemp()
    {
        var count = 0;
        try
        {
            foreach (var d in Directory.EnumerateDirectories(Path.GetTempPath(), WifiTempPrefix + "*"))
            {
                WipeDirectory(d);
                count++;
            }
        }
        catch
        {
            // yoksay
        }
        return count;
    }
}
