# DriverPilot 0.3.5 — persönlicher Testkandidat

**Vorbereitet, nicht veröffentlicht. Keine Produktionsfreigabe.** Die zuletzt veröffentlichte Version bleibt 0.3.4. Ein Versionswert 0.3.5 im Programm ist kein Nachweis einer signierten oder freigegebenen Ausgabe.

## Änderungen gegenüber 0.3.4

- Dezenter Hinweis **Entwickelt von Christian Strube** direkt unter dem App-Namen und unter **Einstellungen → Über DriverPilot**. Keine zusätzliche KI-Autorennennung.
- Bereits zusammengeführter Ferndiagnosevertrag und Client **Hilfe von Christian**: Vorschau und ausdrückliche Zustimmung vor Übertragung; Ergebnisse nur nach Freigabe. Das ist kein Nachweis eines vollständigen echten Serverablaufs.
- Bereits zusammengeführte Deinstallation mit getrennten Löschentscheidungen; siehe [UNINSTALL.md](../UNINSTALL.md). Lokale Installation/Deinstallation gehört nicht zur Vorbereitung dieses Kandidaten.
- Veröffentlichung im Release-Workflow hängt jetzt ausdrücklich von `build`, `app-upgrade` **und** `contract` ab. Eine statische Regression prüft diese Abhängigkeiten; sie führt keinen Workflow aus.

## Lokales Entwicklungsartefakt

`build/Build.ps1` **ohne** `-Sign` erstellt `artifacts/release-0.3.5/` als selbstständigen Windows-x64-Entwicklungsbuild. Kein Installer, keine abgesetzte Produktionssignatur, kein authentifiziertes Updatepaket. Weder `Setup.ps1` ausführen noch Zertifikatsvertrauen oder Wartungsaufträge dafür ändern. Windows-Schutzfunktionen nicht umgehen.

Die Vorbereitung verwendet isolierte WPF-Testoberflächen und synthetische Testmodi statt normalem App-Start. Ein normaler Start kann lokale Zustände, Überwachung und Infobereich initialisieren und ist eine gesonderte bewusste Testentscheidung. Testausgaben gehören auf eine Scratch-Kopie, nicht in das Paketinventar.

Reproduzierbare Prüfungen unter Windows mit .NET SDK 10:

```powershell
dotnet build DriverPilot.slnx -c Debug
dotnet build DriverPilot.slnx -c Release
dotnet run --project tests/Branding/Branding.Tests.csproj -c Debug
dotnet run --project tests/Branding/Branding.Tests.csproj -c Release
dotnet run --project tests/SystemReport/SystemReport.csproj -c Debug -- --integration --ui
dotnet run --project tests/SystemReport/SystemReport.csproj -c Release -- --integration --ui
dotnet run --project tests/RemoteDiagnosis/RemoteDiagnosis.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Test-ReleaseWorkflowContract.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File build/Build.ps1
```

Die Branding-Regression prüft den genauen Text, die Position direkt unter dem Namen, die Einstellungen, typografische Hierarchie und echte isolierte WPF-Geometrie bei der produktiven Seitenleistenbreite. Sie konstruiert das Hauptfenster nicht und ersetzt keine Benutzerabnahme. Ausgeführte Prüfungen und Artefaktnachweise werden separat im Arbeitsbericht angegeben; obige Befehle allein behaupten keinen Erfolg.

## Offene Freigaben und Grenzen

- Drei unabhängige Reviews desselben eingefrorenen Quellstands, tatsächliche UI-Abnahme durch Christian und Server-E2E stehen vor einer Produktionsfreigabe aus.
- Echter Serverablauf: Einladung/Pairing, Datenschutzhinweis, bewusstes Senden, Freigabe/Ergebnis, Feedback und Löschen müssen gesondert mit einem autorisierten Testfall geprüft werden. Externe KI darf nicht aus einem erfolgreichen Capabilities-Abruf abgeleitet werden; maßgeblich sind die angebotenen Fähigkeiten und der aktuelle Datenschutzhinweis.
- Kein lokaler Signier-, Installations-, Zertifikats- oder Wartungsauftragstest. Keine reale Treiberinstallation, kein Rollback und kein BIOS-/Firmwaretest.
- Signierter CI-Testkandidat nur nach gesonderter ausdrücklicher Genehmigung und den dafür vorgesehenen Freigabeprüfungen. Ein Actions-Testartefakt ist kein GitHub Release.
- Kein Commit, Push oder Tag gehört zur lokalen Vorbereitung. Ein `v*`-Tag kann automatisch veröffentlichen und benötigt deshalb eine eigenständige Release-Freigabe. `v0.3.4` niemals versetzen.
