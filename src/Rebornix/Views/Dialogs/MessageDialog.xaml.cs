using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Rebornix.Services;

namespace Rebornix.Views.Dialogs;

public partial class MessageDialog : Window
{
    public int Result { get; private set; } = -1;

    public MessageDialog(DialogKind kind, string title, string message, string[] buttons)
    {
        InitializeComponent();
        TitleText.Text = title;
        MessageText.Text = message;

        var (glyph, brushKey, bgKey) = kind switch
        {
            DialogKind.Warning => ("", "WarningBrush", "WarningSoftBrush"),
            DialogKind.Error => ("", "DangerBrush", "DangerSoftBrush"),
            DialogKind.Danger => ("", "DangerBrush", "DangerSoftBrush"),
            DialogKind.Question => ("", "AccentHoverBrush", "AccentSoftBrush"),
            _ => ("", "InfoBrush", "InfoSoftBrush")
        };
        IconGlyph.Text = glyph;
        IconGlyph.Foreground = (Brush)FindResource(brushKey);
        IconBg.Background = (Brush)FindResource(bgKey);

        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var b = new Button
            {
                Content = buttons[i],
                MinWidth = 96,
                Margin = new Thickness(10, 0, 0, 0),
                IsDefault = i == 0,
                IsCancel = i == buttons.Length - 1 && buttons.Length > 1
            };
            if (i == 0)
                b.Style = (Style)FindResource(kind == DialogKind.Danger ? "DangerButton" : "AccentButton");
            b.Click += (_, _) =>
            {
                Result = index;
                DialogResult = true;
            };
            Buttons.Children.Add(b);
        }

        // Tehlikeli işlemlerde varsayılan (Enter) düğmesi "İptal" olsun
        if (kind == DialogKind.Danger && Buttons.Children.Count > 1)
        {
            ((Button)Buttons.Children[0]).IsDefault = false;
            ((Button)Buttons.Children[^1]).IsDefault = true;
        }

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
