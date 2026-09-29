using System.Windows;
using System.Windows.Input;
using Rebornix.Helpers;
using Rebornix.Services;

namespace Rebornix.Views.Dialogs;

/// <summary>Parola sorar. Parola hiçbir yere kaydedilmez veya loglanmaz.</summary>
public partial class PasswordDialog : Window
{
    private readonly bool _confirm;

    public string Password { get; private set; } = "";

    public PasswordDialog(string title, string message, bool confirm, string? error)
    {
        InitializeComponent();
        _confirm = confirm;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmPanel.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrEmpty(error)) ShowError(error);
        Loaded += (_, _) => Pwd1.Focus();
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => ErrorText.Visibility = Visibility.Collapsed;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var p1 = Pwd1.Password;
        if (_confirm)
        {
            if (p1.Length < WifiCrypto.MinPasswordLength)
            {
                ShowError(Loc.F("Pwd_TooShort", WifiCrypto.MinPasswordLength));
                return;
            }
            if (p1 != Pwd2.Password)
            {
                ShowError(Loc.Get("Pwd_Mismatch"));
                return;
            }
        }
        else if (p1.Length == 0)
        {
            ShowError(Loc.Get("Pwd_Empty"));
            return;
        }

        Password = p1;
        Pwd1.Clear();
        Pwd2.Clear();
        DialogResult = true;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
