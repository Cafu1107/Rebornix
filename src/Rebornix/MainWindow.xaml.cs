using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Rebornix.Models;
using Rebornix.Services;

namespace Rebornix;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => UseDarkTitleBar();
        // Küçük pencerede içerik alanına daha çok yer kalsın
        SizeChanged += (_, _) => LogList.Height = ActualHeight < 760 ? 76 : 130;
        Log.EntryAdded += OnLogAdded;
        Closed += (_, _) => Log.EntryAdded -= OnLogAdded;
    }

    /// <summary>Canlı log paneli en son satıra kayar.</summary>
    private void OnLogAdded(LogEntry entry)
    {
        if (!LogList.IsVisible || LogList.Items.Count == 0) return;
        LogList.ScrollIntoView(LogList.Items[^1]);
    }

    private void UseDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var on = 1;
            // DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1+ = 20, eski sürümler = 19)
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }
        catch
        {
            // Eski Windows: varsayılan başlık çubuğu
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
