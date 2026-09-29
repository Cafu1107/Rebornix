# Rebornix

Portable Windows tool (C# 12, .NET 8, WPF, MVVM via CommunityToolkit.Mvvm): back up before a Windows reinstall, restore afterwards, bulk-install apps via winget.

## Layout

- `src/Rebornix/Services/`: all system work (drivers, Wi-Fi + `WifiCrypto`, winget, Ludusavi game saves, Windows settings, `ProcessRunner`).
- `src/Rebornix/ViewModels/`, `Views/`: one ViewModel/View pair per sidebar page.
- `src/Rebornix/Resources/i18n/{en,tr,de}.txt`: all UI text, one `Key=Value` per line. English is the default language. Edit the `.txt` files, then run `tools/gen-resx.ps1` to regenerate `Strings.resx` / `Strings.tr.resx` / `Strings.de.resx` (it fails on missing keys or placeholder mismatches). No hard-coded user-facing strings in XAML/C#.
- `src/Rebornix/Themes/`: `Colors.Dark.xaml` and `Colors.Light.xaml` (same keys, enforced by a test) + `Styles.xaml`. `Services/ThemeManager.cs` loads them; language/theme changes rebuild the main window via `App.ReloadUi` (no UAC prompt).
- `src/Rebornix/Data/catalog.json`: winget app catalog, embedded in the exe. Category = English ID (display name from `Cat_*` strings); `description` (en), `description_tr`, `description_de`.
- `tests/Rebornix.Tests/`: xUnit.

## Commands

```bash
dotnet build
dotnet test -c Release        # must stay green; CI runs this on every push
dotnet build src/Rebornix -p:SmokeManifest=true && Rebornix.exe --smoke <dir> [--lang en|tr|de] [--theme Dark|Light]   # UI smoke run + README screenshots, no UAC
```

## Safety rules (this is the developer's real PC)

- Never really run driver install, Wi-Fi profile add or Windows-settings restore here. Use Dry Run only.
- Settings tests write only to `HKCU\Software\RebornixTest_*`.
- A normal build uses `requireAdministrator` and triggers a UAC prompt on the user's screen. Use `-p:SmokeManifest=true` for automated runs.
- Never print or log Wi-Fi passwords.
- Ludusavi integration tests run only when `RBX_LUDUSAVI_EXE` is set.

## Conventions

- The owner does not code and reads Turkish: explain changes in plain Turkish.
- README.md is English (main GitHub page); README.tr.md is the Turkish version. Keep both in sync. Never publish screenshots showing the owner's real backup (computer name, user name, hardware).
- Commit identity: `Cafu1107 <179776180+Cafu1107@users.noreply.github.com>`. Commit messages are in Turkish.
- Releases: bump `<Version>` in the csproj, then push a `vX.Y.Z` tag. `.github/workflows/release.yml` builds `Rebornix.exe` + `.sha256`.
