# Isolated Windows icon verification

Run on Windows with the .NET 10 SDK:

```powershell
dotnet run --project tests/AppIcon/AppIcon.Tests.csproj -c Debug
dotnet run --project tests/AppIcon/AppIcon.Tests.csproj -c Release
# Optional argument: inspect the native resources of a published apphost without launching it.
dotnet run --project tests/AppIcon/AppIcon.Tests.csproj -c Release -- C:\scratch\app\DriverPilot.exe
```

For single-file resource loading verification, publish this test project with `-r win-x64 --self-contained true -p:PublishSingleFile=true -o <scratch-test-directory>`, then run `AppIcon.Tests.exe <published-DriverPilot.exe-path>`.

The harness never constructs DriverPilot.App or MainWindow. It opens and closes a minimal WPF window and temporarily shows a NotifyIcon using the production loader; no Store, scan, timer, installer, autostart, or maintenance initialization runs. Tests cover all nine ICO frame sizes, frozen/cache-loaded WPF imagery, real small/large WM_GETICON handles, tray handle validity across GC, owned-handle disposal, and ExtractIconEx pixel equality with the embedded brand asset. Static source guards cover the MainWindow wiring and disposal order without executing its side effects.

Installer verification is configuration-only: the harness checks SetupIconFile, but does not compile, sign, execute, or install an installer. The full MainWindow layout and Explorer/taskbar icon caches are not visually tested by this harness.
