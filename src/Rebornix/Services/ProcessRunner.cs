using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Rebornix.Services;

public sealed record ProcResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
    public string Combined => string.IsNullOrWhiteSpace(Error) ? Output : Output + Environment.NewLine + Error;
}

/// <summary>Harici komutları (pnputil, netsh, winget, PowerShell, Ludusavi) asenkron ve iptal edilebilir çalıştırır.</summary>
public static class ProcessRunner
{
    static ProcessRunner()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>netsh / pnputil gibi konsol araçlarının kullandığı OEM kod sayfası (Türkçe: 857).</summary>
    public static Encoding Oem
    {
        get
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { return Encoding.UTF8; }
        }
    }

    public static string SystemExe(string name) => Path.Combine(Environment.SystemDirectory, name);

    public static string PowerShellExe =>
        Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");

    /// <param name="logCommand">Komut satırı loga yazılsın mı (hassas argüman varsa false).</param>
    public static async Task<ProcResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken ct = default,
        Encoding? encoding = null,
        string? stdin = null,
        Action<string>? onOutput = null,
        bool logCommand = true,
        string? workingDirectory = null)
    {
        ct.ThrowIfCancellationRequested();
        encoding ??= Oem;
        if (logCommand) Log.Debug($"> {Path.GetFileName(fileName)} {arguments}");

        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
            WorkingDirectory = workingDirectory ?? Environment.SystemDirectory
        };
        if (stdin is not null) psi.StandardInputEncoding = new UTF8Encoding(false);

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var sbOut = new StringBuilder();
        var sbErr = new StringBuilder();
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (sbOut) sbOut.AppendLine(e.Data);
            onOutput?.Invoke(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (sbErr) sbErr.AppendLine(e.Data);
        };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (stdin is not null)
        {
            await p.StandardInput.WriteAsync(stdin);
            p.StandardInput.Close();
        }

        await using (ct.Register(() =>
                     {
                         try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
                         catch { /* süreç zaten kapanmış olabilir */ }
                     }))
        {
            await p.WaitForExitAsync(CancellationToken.None);
        }

        ct.ThrowIfCancellationRequested();
        string o, e2;
        lock (sbOut) o = sbOut.ToString();
        lock (sbErr) e2 = sbErr.ToString();
        return new ProcResult(p.ExitCode, o, e2);
    }

    /// <summary>Windows PowerShell 5.1 betiği çalıştırır; çıktı UTF-8 okunur.</summary>
    public static Task<ProcResult> PowerShellAsync(string script, CancellationToken ct = default, bool logScript = false)
    {
        var full = "$ProgressPreference='SilentlyContinue'; $OutputEncoding=[System.Text.Encoding]::UTF8; " +
                   "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; " + script;
        if (logScript) Log.Debug("PowerShell: " + script);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return RunAsync(PowerShellExe,
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            ct, Encoding.UTF8, logCommand: false);
    }
}
