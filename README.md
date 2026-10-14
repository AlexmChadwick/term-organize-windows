# Term Organize for Windows

Unofficial Windows desktop app for searching Northeastern University courses, building schedules, planning a degree, and watching seats. It is a port of the Mac and iPhone app [Term Organize](https://github.com/AlexmChadwick/Northeastern-Term-Organize).

**Not affiliated with, endorsed by, or sponsored by Northeastern University or Sandbox.** Course information is © Northeastern University and is not covered by the license grant. See [NOTICE.md](NOTICE.md) and [LICENSE](LICENSE).

The window follows the Mac app: one window, a sidebar (Search, Subjects, Saved, Professors, Free Rooms, Schedules, Degree Plans, Watchlist), a term picker in the title bar, and the unofficial-status line along the bottom. Light and dark themes use Fluent styling and a Mica backdrop. Closing the window leaves the app in the tray; Exit on the tray menu quits. The tray flyout is Up Next.

All of the course, schedule, and degree logic lives in `src/TermOrganize.Core`, a .NET 9 class library that builds and tests on Linux. The WinUI 3 shell in `src/TermOrganize.App` is what Windows packages. On Linux, `dotnet build src/TermOrganize.App/TermOrganize.App.csproj -p:EnableWindowsTargeting=true -p:Platform=x64` compiles that shell's C# against the Windows App SDK reference assemblies. The XAML compiler does not run on Linux. How each Swift area was carried over is in [PARITY.md](PARITY.md).

## Build the logic (Linux, macOS, or Windows)

Requires the .NET 9 SDK.

```bash
dotnet test tests/TermOrganize.Core.Tests/TermOrganize.Core.Tests.csproj
```

On a machine that is not Windows, `dotnet build src/TermOrganize.App/TermOrganize.App.csproj` succeeds without compiling the WinUI shell. Add `-p:EnableWindowsTargeting=true -p:Platform=x64` to compile the shell C# against the Windows App SDK references.

## Build the app (Windows)

Requires the .NET 9 SDK and Windows 10 version 2004 (build 19041) or later.

```powershell
dotnet publish src/TermOrganize.App/TermOrganize.App.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:WindowsAppSdkBootstrapInitialize=false -p:WindowsPackageType=None -p:EnableMsixTooling=true -p:GenerateAppxPackageOnBuild=false -o artifacts/unpackaged
```

Run `artifacts\unpackaged\TermOrganize.App.exe`. An unpackaged build registers `termorganize://`, `neuclasssearch://`, and .pdf Open With for the current user.

GitHub Actions (`.github/workflows/windows.yml`) on `windows-latest` runs the core tests and, in a separate job, publishes a signed MSIX and a self-contained unpackaged zip (`WindowsAppSDKSelfContained=true`). That job waits for `TermOrganize.App.exe --screenshot-sweep` to exit and uploads light and dark PNGs of each page at 1440×900 and 800×900, plus `error.txt` and the sweep log, as the `ui-sweep` artifact even when the capture fails. The package job runs even when the tests fail. The workflow creates a self-signed `CN=Term Organize` certificate at build time. That certificate is not in source control. Trust it before installing the MSIX. The shell C# has been compiled on Linux against those references. The XAML compiler does not run on Linux.

## Data

User data is a JSON file at `%LOCALAPPDATA%\TermOrganize\user-data.json`. Settings can export and import it. The bundled catalog is Fall 2026 (`term-202710`), as gzip and as the Mac app's LZFSE `.catalog`. New catalogs this port writes are gzip. Live sections come from `nubanner.neu.edu`. Degree requirements are parsed from `catalog.northeastern.edu` HTML.

## Keyboard

Ctrl+1 through Ctrl+8 switch sidebar areas. Ctrl+N new schedule, Ctrl+Shift+N new degree plan, Ctrl+O import a degree-audit PDF, Ctrl+E export, Ctrl+P print the degree plan, Ctrl+R reload, Ctrl+, settings, Alt+Ctrl+0 show or hide the sidebar.
