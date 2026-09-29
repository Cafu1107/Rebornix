using System.Windows;
using System.Windows.Input;

namespace Rebornix.Views.Dialogs;

public partial class InputDialog : Window
{
    public string Value { get; private set; } = "";

    public InputDialog(string title, string prompt, string initial)
    {
        InitializeComponent();
        TitleText.Text = title;
        PromptText.Text = prompt;
        ValueBox.Text = initial;
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var v = ValueBox.Text.Trim();
        if (v.Length == 0) return;
        Value = v;
        DialogResult = true;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
