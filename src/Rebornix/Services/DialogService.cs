using System.Windows;
using Rebornix.Helpers;
using Rebornix.Views.Dialogs;

namespace Rebornix.Services;

public enum DialogKind { Info, Warning, Error, Question, Danger }

/// <summary>Koyu temalı modal diyaloglar. Tüm çağrılar arayüz iş parçacığına yönlendirilir.</summary>
public sealed class DialogService
{
    /// <summary>Otomatik arayüz testinde (--smoke) diyaloglar açılmaz, varsayılan cevap döner.</summary>
    public bool AutoMode { get; set; }

    /// <summary>Yalnızca uçtan uca testlerde: AutoMode açıkken her soruya ilk düğmeyle (evet/devam) cevap verir.</summary>
    internal bool AutoConfirm { get; set; }

    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    private static T OnUi<T>(Func<T> f)
    {
        var d = Application.Current?.Dispatcher;
        return d is null || d.CheckAccess() ? f() : d.Invoke(f);
    }

    /// <returns>Seçilen düğmenin sırası; kapatılırsa -1.</returns>
    public int Show(DialogKind kind, string title, string message, params string[] buttons)
    {
        Log.Debug($"[Diyalog] {title}");
        if (AutoMode) return AutoConfirm ? 0 : -1;
        return OnUi(() =>
        {
            var dlg = new MessageDialog(kind, title, message, buttons) { Owner = Owner };
            dlg.ShowDialog();
            return dlg.Result;
        });
    }

    public void Info(string title, string message) => Show(DialogKind.Info, title, message, Loc.Get("Btn_Ok"));
    public void Warn(string title, string message) => Show(DialogKind.Warning, title, message, Loc.Get("Btn_Ok"));
    public void Error(string title, string message) => Show(DialogKind.Error, title, message, Loc.Get("Btn_Ok"));

    public bool Confirm(string title, string message, string? yes = null, string? no = null, bool danger = false) =>
        Show(danger ? DialogKind.Danger : DialogKind.Question, title, message,
            yes ?? Loc.Get("Btn_Continue"), no ?? Loc.Get("Btn_Cancel")) == 0;

    /// <summary>Parola ister. confirm=true ise iki kez sorulur ve "unutursan açılamaz" uyarısı gösterilir.</summary>
    public string? AskPassword(string title, string message, bool confirm, string? error = null)
    {
        if (AutoMode) return null;
        return OnUi(() =>
        {
            var dlg = new PasswordDialog(title, message, confirm, error) { Owner = Owner };
            return dlg.ShowDialog() == true ? dlg.Password : null;
        });
    }

    public string? AskText(string title, string prompt, string initial = "")
    {
        if (AutoMode) return null;
        return OnUi(() =>
        {
            var dlg = new InputDialog(title, prompt, initial) { Owner = Owner };
            return dlg.ShowDialog() == true ? dlg.Value : null;
        });
    }
}
