using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        Loaded += (_, _) => Dispatcher.BeginInvoke(PlayIntroAnimations, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Logonun arkasında nabız gibi atan ışık + menü öğelerinin sırayla kayarak gelmesi.</summary>
    private void PlayIntroAnimations()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var pulse = new DoubleAnimation(1.0, 1.45, TimeSpan.FromSeconds(1.8))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        LogoPulseScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        LogoPulseScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        LogoPulse.BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, 0.25, TimeSpan.FromSeconds(1.8))
        {
            AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever
        });

        for (var i = 0; i < NavList.Items.Count; i++)
        {
            if (NavList.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement item) continue;
            var shift = new TranslateTransform(-22, 0);
            item.RenderTransform = shift;
            item.Opacity = 0;
            var delay = TimeSpan.FromMilliseconds(120 + i * 60);
            item.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = delay });
            shift.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-22, 0, TimeSpan.FromMilliseconds(480)) { BeginTime = delay, EasingFunction = ease });
        }
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
