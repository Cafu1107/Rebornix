# Rebornix

Portable Windows tool (C# 12, .NET 8, WPF, MVVM via CommunityToolkit.Mvvm): back up before a Windows reinstall, restore afterwards, bulk-install apps via winget.

## Layout

- `src/Rebornix/Services/`: all system work (drivers, Wi-Fi + `WifiCrypto`, winget, Ludusavi game saves, Windows settings, `ProcessRunner`).
- `src/Rebornix/ViewModels/`, `Views/`: one ViewModel/View pair per sidebar page.
- `src/Rebornix/Resources/Strings.resx`: all UI text (Turkish). No hard-coded strings in XAML/C#.
- `src/Rebornix/Data/katalog.json`: winget app catalog, embedded in the exe.
- `tests/Rebornix.Tests/`: xUnit.

## Commands

```bash
dotnet build
dotnet test -c Release        # must stay green; CI runs this on every push
dotnet build src/Rebornix -p:SmokeManifest=true && Rebornix.exe --smoke <dir>   # UI smoke run + README screenshots, no UAC
```

## Safety rules (this is the developer's real PC)

- Never really run driver install, Wi-Fi profile add or Windows-settings restore here. Use Dry Run only.
- Settings tests write only to `HKCU\Software\RebornixTest_*`.
- A normal build uses `requireAdministrator` and triggers a UAC prompt on the user's screen. Use `-p:SmokeManifest=true` for automated runs.
- Never print or log Wi-Fi passwords.
- Ludusavi integration tests run only when `RBX_LUDUSAVI_EXE` is set.

## Conventions

- The owner does not code and reads Turkish: explain changes in plain Turkish.
- Commit identity: `Cafu1107 <179776180+Cafu1107@users.noreply.github.com>`. Commit messages are in Turkish.
- Releases: bump `<Version>` in the csproj, then push a `vX.Y.Z` tag. `.github/workflows/release.yml` builds `Rebornix.exe` + `.sha256`.
