using System.Runtime.CompilerServices;
using Rebornix.Services;

namespace Rebornix.Tests;

internal static class TestSetup
{
    public static readonly string Root = Path.Combine(Path.GetTempPath(), "RebornixTests_" + Environment.ProcessId);

    /// <summary>Testlerde uygulama klasörü (log, Data, Tools) geçici klasöre yönlendirilir.</summary>
    [ModuleInitializer]
    internal static void Init()
    {
        Directory.CreateDirectory(Root);
        AppPaths.OverrideBase(Root);
        AppPaths.EnsureBaseFolders();
    }

    public static string NewDir(string name)
    {
        var d = Path.Combine(Root, name + "_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }
}
