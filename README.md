# DriverPilot 0.3.4

Die veröffentlichte Version [0.3.4](https://github.com/Madchristian/DriverPilot/releases/tag/v0.3.4) ergänzt herstellerunabhängige Hardwareprofile, datensparsame Systemberichte und das neue Chip-Icon. Release-Dateien sind seit 0.3.2 signiert, das vollständige App-Updateinventar ist seit 0.3.3 durch ein signiertes Manifest authentifiziert. Migration von veröffentlichten Versionen bis 0.3.2 nur über den unabhängig geprüften signierten Installer. Nachweise und Testgrenzen: [aktueller Validierungsbericht](https://github.com/Madchristian/DriverPilot/blob/main/docs/validation/VALIDATION-SIGNED.md), [Release-Notizen](https://github.com/Madchristian/DriverPilot/blob/main/docs/releases/RELEASE-0.3.4.md) und [Update-Sicherheit](https://github.com/Madchristian/DriverPilot/blob/main/docs/security/UPDATE-SECURITY.md). Spätere CI-/Betriebsänderungen auf main sind nicht automatisch Bestandteil des veröffentlichten Tags.

Windows-App für lokale Hardwarediagnose, Geräte-/Treiberüberwachung und konservative Treiberwartung. Für einen selbst zusammengestellten Windows-11-Gaming-PC mit Schwerpunkt Stabilität.

## Start

### Für Freunde: Download, Vertrauen und Updates

1. [GitHub-Releases](https://github.com/Madchristian/DriverPilot/releases) mit einem berechtigten GitHub-Konto öffnen; Repository und Downloads sind privat. Alternativ die unveränderten Release-Dateien direkt von Christian beziehen.
2. Vor dem ersten Start `DriverPilot.cer` und die [Zertifikatanleitung](https://github.com/Madchristian/DriverPilot/blob/main/docs/security/ZERTIFIKAT-ANLEITUNG.txt) lesen, den Fingerabdruck unabhängig mit Christian abgleichen, Vertrauen bewusst einrichten und die digitale Signatur der Setup-EXE prüfen. Das Setup importiert kein Zertifikat. Windows-Schutzfunktionen nicht abschalten.
3. **Erstinstallation oder Migration von 0.3.2 und älter:** den so geprüften signierten `DriverPilot-<Version>-Setup-x64.exe` starten. Der Installer richtet auch die Wartungsautomatik ein; anschließend Regeln und Zeitfenster unter **Wartung & Sperren** prüfen oder die Automatik in den Einstellungen deaktivieren. Kein heruntergeladenes `Setup.ps1` direkt ausführen.
4. **Update einer geschützten Installation ab 0.3.3:** das signierte Release-ZIP einer strikt neueren Version vollständig in einen eigenen Ordner entpacken, dann **Einstellungen → Updatepaket auswählen …** verwenden. Den Ordner unverändert lassen; die App prüft Herausgeber, Version und vollständigen Inhalt vor der Administratorfreigabe. Bei Zertifikatswechsel oder unklarer Herkunft Christian kontaktieren; neues Windows-Vertrauen allein ändert den eingebauten Updater-Pin nicht.
5. **Nur portable Diagnose:** signiertes ZIP entpacken, EXE-Signatur prüfen und `DriverPilot.exe` öffnen. Portable Nutzung ist keine geschützte Installation und darf keine lokalen Skripte für Einrichtung oder App-Updates erhöhen.

`DriverPilot.exe` öffnen. Geräte und ausgewählte Windows-Systemereignisse werden gelesen. Schließen minimiert in den Infobereich; dort lässt sich die App beenden. Die Diagnose läuft ohne Administratorrechte.

Die erste Installation erfolgt ausschließlich über den unabhängig geprüften, signierten Installer (siehe [Update-Sicherheit](https://github.com/Madchristian/DriverPilot/blob/main/docs/security/UPDATE-SECURITY.md)). Er kopiert die App nach `C:\Program Files\DriverPilot`, legt geschützte Wartungsdaten unter `C:\ProgramData\DriverPilot` an und registriert `DriverPilot Maintenance`. **Einstellungen → Automatik einrichten** kann dort die Einrichtung mit Windows-Administratorabfrage erneut vornehmen. Portable Builds dürfen keine Skripte erhöht starten. Die Einrichtung ist für ein lokales Administratorkonto vorgesehen. Kein SYSTEM-Dienst und keine fremden Administrator-Anmeldedaten verwenden.

## Funktionsumfang

- Tatsächliche Windows-Geräte, Treiberversionen, Problemcodes und Hardwareübersicht.
- Wiederkehrende Prüfung alle zwei Minuten (inklusive Gerätestatus); Treiberversionsänderungen werden protokolliert.
- Ausgewählte Systemereignisse der letzten 24 Stunden: Kernel-PnP, Display/NVIDIA/AMD, WHEA, Disk/NVMe/AHCI. Keine vollständige Windows-Protokoll- oder Crashdumpanalyse.
- Lokale Benachrichtigungen, Deduplizierung und als bekannt markierbare Befunde.
- Echte Treibersuche über die Windows Update Agent COM API.
- Nach Einrichtung Wartungsauftrag alle 30 Minuten; Installation im einstellbaren Zeitfenster (Standard Sonntag 12–17 Uhr), wenn seit mindestens zehn Minuten keine Eingabe erfolgte und kein erkanntes Spiel bzw. keine Vollbildanwendung aktiv ist. Spiele-Erkennung ist heuristisch und deckt nicht jedes Spiel ab. Bei aktiver Nutzung wird auf den nächsten passenden Lauf verschoben. Eine bereits laufende Installation wird bei Rückkehr des Benutzers nicht abgebrochen.
- Mindestens 14 Tage seit erster Beobachtung der konkreten Paketrevision und seit letzter Paketänderung. Diese Zeit ist keine Garantie für Fehlerfreiheit.
- Nur Windows-Update-Pakete ausgewählter Geräteklassen, bestätigter Hardwarezuordnung und ohne interaktive Installationsanforderung. Deaktivierte Geräte bleiben unberührt. Firmware, BIOS, Speichertreiber und Vorabversionen werden ausgenommen.
- Export vorhandener Drittanbietertreiber vor der Installation. Das ist keine vollständige Systemsicherung und keine garantierte Rückrollfunktion. Sicherungen liegen in ProgramData/DriverPilot/Backups und werden nicht automatisch gelöscht.
- Pakete einzeln installieren; nach Fehler, unbekanntem vorherigen Installationsergebnis oder ausstehendem Neustart stoppen. Kein Neustartbefehl ist implementiert.
- Codex-Anbindung über offiziellen lokalen App Server mit Browseranmeldung; eigener Anmeldespeicher in LocalAppData/DriverPilot/Codex. Kein Übernehmen bestehender Codex-Tokens.
- KI erhält bei Senden Gerätetypen, Treiberversionen und gekürzte Diagnosemetadaten. Keine Hardware-IDs, Seriennummern oder vollständigen Ereignisnachrichten. KI kann den Gerätestatus lesen, Windows Update durchsuchen und den eingerichteten Wartungsauftrag anstoßen. Die privilegierte Wartung läuft ausschließlich über feste Programmregeln; KI kann weder freie Befehle noch beliebige Installer an den Auftrag übergeben.

## Hersteller & Firmware

### Neu in 0.3.4: fremde PCs und Systembericht

Version 0.3.4 erweitert die Hardwareerfassung um Systemhersteller/-modell, Mainboardhersteller/-modell/-revision, BIOS-Datum, Architektur und Windows-Build. ASUS, MSI, Gigabyte, ASRock, Dell, HP, Lenovo und Acer werden über gemeinsame Herstelleradapter zugeordnet. Bei OEM-Rechnern und Notebooks hat der Systemhersteller Vorrang. Unbekannte oder unvollständige Zuordnungen bleiben ausdrücklich offen.

Neue Herstellerwege sind **manuelle offizielle Portale**, keine zugesagte modellgenaue Unterstützung. Es liegen noch keine bestätigten ASUS-/Freundes-Konfigurationen vor. Der bekannte MSI-/X670E-Pfad bleibt eng an die verifizierte Modellzuordnung gebunden; automatische Windows-11-Paketvergleiche setzen eine bestätigte x64-/Windows-11-Identität voraus. BIOS und Firmware werden nicht geflasht.

Unter **Hardware & Treiber → Systembericht teilen** lässt sich nach einem erfolgreichen frischen Scan eine lokale Vorschau öffnen. Der Bericht enthält ausschließlich ausgewählte strukturierte Hardwarefelder, App-/Windows-Version, relevante Geräteklassen mit bereinigten PCI-/USB-Modellkennungen und optionalen Benutzerangaben. GPU-Zuordnung erfolgt über die Modellkennung statt über frei formulierte Gerätenamen. Rohe Ereignisse, Chat, Einstellungen und vollständige Geräteinstanz-IDs werden nicht exportiert. Die Filter sind konservative Heuristiken, keine Garantie perfekter Anonymität: Vorschau und eigene Angaben vor dem Teilen vollständig prüfen.

- **Lokal speichern** funktioniert ohne Netzwerk und GitHub-Konto. Die Datei enthält exakt den Vorschautext; der Speicherort wird ausgewählt.
- **GitHub-Entwurf öffnen** erfordert eine bewusste Zustimmung und öffnet nur einen vorausgefüllten Browserentwurf. Das Repository ist privat; GitHub-Konto und Zugriff sind erforderlich. Das Issue veröffentlicht der Nutzer selbst.
- Bereits die Entwurfs-URL übermittelt den Bericht an Browser/GitHub und kann im Browserverlauf stehen. Kein automatischer Upload, eingebauter Token oder Clipboard-Zugriff.
- Entwurfs-URLs über 2.000 Zeichen werden nicht geöffnet oder abgeschnitten. In diesem Fall die vollständige Datei speichern und bewusst manuell weitergeben.
- Fehlende, fehlgeschlagene oder über 15 Minuten alte Scans müssen erneuert werden. Herstellerprüfungen werden nicht aus fremden/veralteten gespeicherten Berichten übernommen.

Die Funktion ersetzt nicht den umfassenderen Diagnoseexport mit begrenzten Protokollen aus Issue #7. Echte Freundesprofile und deren praktische Abnahme bleiben für Issue #10 erforderlich. Das neue Chip-Icon wird in EXE, Fenster/Taskleiste, Infobereich und Installer verwendet.

Der zusätzliche Bereich prüft unterstützte offizielle Herstellerquellen täglich, solange die Überwachung im Infobereich läuft, oder über „Herstellerquellen prüfen“. Codex kann dieselbe Prüfung mit `driverpilot_manufacturer_updates` aufrufen. Hardwaremodelle werden lokal erkannt; keine Seriennummern werden benötigt.

- **AMD X670E:** vergleicht das offizielle Chipsatzpaket mit der tatsächlich installierten Paketversion aus der Windows-Programmliste. Einzelne Chipsatztreiberversionen werden nicht mit der Paketversion vermischt.
- **NVIDIA:** ermittelt das exakte GPU-Modell und Windows 11 über den Katalog der offiziellen NVIDIA-Webseite. Vergleich mit dem Game-Ready-WHQL-Paket, ohne Beta-/Vorabversionen. Die Windows-Treiberversion wird in das NVIDIA-Schema umgerechnet.
- **MSI MPG X670E CARBON WIFI:** erfasst BIOS-Version und Datum sowie zugehörige LAN-/WLAN-/Bluetooth-Treiber. Ruft die genaue MSI-Supportseite ab. Diese blockiert derzeit automatisierte Zugriffe (HTTP 403); deshalb wird kein erfundener BIOS-Versionsvergleich angezeigt. „Offizielle Quelle öffnen“ führt zur manuellen Prüfung im Browser.
- **WD_BLACK SN850X:** zeigt die lokale Firmware. Die korrekte Firmwareprüfung erfolgt über SANDISK Dashboard, da ein Modell verschiedene Firmwarezweige haben kann. Kein öffentlicher Versionsvergleich wird vorgetäuscht.
- **Corsair MP600 / MP600 PRO LPX:** zeigt die lokale Firmware und verlinkt die offizielle Anleitung zur SSD Toolbox. Die neue Firmware muss dort für das konkrete Laufwerk geprüft werden.
- **Creative ZxR:** prüft, ob der offizielle Katalog das passende Treiberpaket enthält. Ohne eindeutig vergleichbare Versionsdaten bleibt der Vergleich ausdrücklich offen.

Neue belegte Treiberversionen können eine Benachrichtigung auslösen. Ein offener Vergleich oder eine gesperrte Quelle bedeutet nicht „aktuell“. Datum und Quelle sind sichtbar, ein älterer zwischengespeicherter Bericht ist mit seinem Prüfzeitpunkt gekennzeichnet. BIOS, SSD-Firmware und Hersteller-EXE-Pakete werden durch diese Suche weder heruntergeladen noch installiert; die bestehende Windows-Update-Automatik wird nicht erweitert.

## Abgeschlossene 0.3-Basis und nachfolgende Absicherung

Die acht ursprünglichen Arbeitsbereiche sind seit 0.3.0 umgesetzt; das bedeutet keine vollständige reale Treiber-/Rollback-Abnahme. Die folgende Übersicht enthält auch die nachfolgende Update-Absicherung ab 0.3.3. Offene Folgearbeiten sind getrennt im [Projektplan und den Milestones](https://github.com/Madchristian/DriverPilot/blob/main/docs/planning/PLAN.md) geführt, nicht als noch ausstehende 0.3-Basis.

- **Wartung & Sperren:** Wochentage, Stunden, 14–90 Tage Paketbeobachtung, Pause bis morgen, eigene Spiele-/Streaming-Prozessnamen und Netzwerklastgrenze. Der erhöhte Worker liest die Regeln erneut vor jedem Paket und unmittelbar vor der Installation. Ungültige Regeln verhindern die Wartung. Eine laufende Installation wird nicht abgebrochen.
- **Hardware → Gerät auf Version halten:** verhindert DriverPilot-Updates für dieses konkrete Geräteexemplar. **Treiberupdates → Update sperren:** blockiert das ausgewählte Windows-Update-Paket über seine Update-ID (einschließlich Revisionen). Sperren lassen sich unter Wartung aufheben. Sie verändern keine Richtlinien von Windows Update oder Hersteller-Tools.
- **Nach Update beobachten:** vergleicht den letzten gespeicherten Gerätestand mit dem neuen Scan, erkennt Versionsänderungen und sammelt 24 Stunden ab Erkennung zusätzliche Meldungen. Auch wiederholte Ereignisse in bereits bekannten Gruppen werden berücksichtigt. Benachrichtigung bei zusätzlichen Befunden. Beobachtung nur während laufender GUI; ein spät erkannter Wechsel hat keinen rückwirkend vollständigen Verlauf. Die Ansicht beweist keine Kausalität oder Fehlerfreiheit.
- **Diagnose:** Systemereignisse nach Quelle, Ereignis-ID, verfügbarer Gerätezuordnung und Stunde gruppieren; WLAN-Abbrüche ergänzen, sofern das optionale Protokoll aktiv ist. Disk 158 mit aktueller Win32_DiskDrive-Zuordnung; Datenträgernummern können sich ändern. Nicht jede Ereignisquelle enthält eine Geräte-ID. Keine vollständige Netzwerk- oder Crashdumpüberwachung.
- **Wiederherstellung:** vorhandene INF-Sicherungen auflisten, Ordner und Geräte-Manager öffnen, unterstützten manuellen Rückweg erklären. Zusätzlich dokumentiert der Worker Treiberversionen/INF-Namen vor und nach jeder Installation unter `ProgramData/DriverPilot/Audit`. Rückrollen ist von Windows und vom Treiber abhängig.
- **Hersteller:** zusätzliche manuelle Portale für AMD-Grafik und Intel sowie SSD-Hinweise für Samsung und Crucial. Diese sind ausdrücklich keine automatischen Versionsvergleiche. Geführte Prüfung pro Eintrag mit Modellabgleich, Herstelleranleitung, Vorbereitung und gespeicherter Benutzernotiz. MSI-M-FLASH: https://www.msi.com/support/technical_details/MB_BIOS_Update ; Samsung: https://semiconductor.samsung.com/consumer-storage/support/tools/ ; Intel: https://www.intel.com/content/www/us/en/download-center/home.htm .
- **Codex:** ausgewählten Befund datensparsam zum Erklären vorbereiten; Benutzer sendet ihn selbst. Anweisungen trennen lokale Fakten, Herstellerangaben und Hypothesen. Lokaler Chat wird gespeichert (maximal 200.000 Zeichen), kann gelöscht werden; nach App-Neustart wird die Anzeige wiederhergestellt, die neue KI-Sitzung kennt ältere Gespräche nicht automatisch. Tool-Protokoll enthält Start, Erfolg/Fehler und tatsächliches Ergebnis (maximal 100 Einträge, 20.000 Zeichen pro Ergebnis).
- **App-Updates:** Die geschützte installierte App akzeptiert nur Pakete mit RSA-SHA256-signiertem Manifest vom fest hinterlegten Herausgeberzertifikat. Produkt, exakte Version, vollständiges Dateiinventar und SHA-256-Hashes sind authentifiziert. Die Administratorabfrage ist an den SHA-256-Wert des ausdrücklich genehmigten Manifests gebunden. Der bereits installierte Updater prüft erneut und prüft nochmals die geschützte Zwischenkopie; kein Kandidatenskript wird gestartet. Austausch erfolgt als vollständiger Ordner, nicht als Datei-Overlay. Der vorige Ordner bleibt als `Program Files/DriverPilot.previous-*` erhalten. App-Neustart erfolgt als normaler Benutzer; kein Windows-Neustart. **Migration von 0.3.2 und älter nur mit unabhängig geprüftem signiertem Installer**, nicht mit deren altem Updatepfad. Details und Grenzen: [Update-Sicherheit](https://github.com/Madchristian/DriverPilot/blob/main/docs/security/UPDATE-SECURITY.md).

## Weitere Grenzen

Automatische Herstellerdownloads außerhalb Windows Update, KI-gestützte Onlineprüfung bekannter Versionsprobleme, automatisches Rollback, Crashdumpanalyse und ein vollständiger Windows-Dienst sind noch nicht implementiert. Nicht jede Hardware hat bereits einen Herstelleradapter. Die App behauptet daher nicht, sämtliche Treiber als aktuell oder ein System als stabil nachgewiesen zu haben. Die KI benötigt ein passendes ChatGPT-/Codex-Konto sowie eine installierte offizielle Codex CLI. Kontolimits gelten weiterhin.

Das überwachte WLAN kann absichtlich deaktiviert sein; Code 22 wird als Hinweis behandelt. Ein Ereignis beweist keinen Treiberdefekt. Eine zeitliche Häufung beweist keine Ursache.

## Daten und Betrieb

Benutzereinstellungen, Diagnose-Snapshot und maximal 500 Verlaufseinträge: `%LOCALAPPDATA%\DriverPilot`. Wartungsstatus, Paketbeobachtung und maximal 100 Installationsresultate: `%PROGRAMDATA%\DriverPilot`. Windows-Benachrichtigungen hängen von den Windows-Benachrichtigungseinstellungen ab. Die GUI muss für Benachrichtigungen im Infobereich laufen. Der Wartungsauftrag benötigt eine angemeldete Sitzung.

Die Automatik lässt sich über Einstellungen deaktivieren. Eine verriegelte fehlerhafte/unterbrochene Installation wird nicht automatisch erneut versucht. Erst nach manueller Prüfung darf ein Administrator den `Pending`-Eintrag in der geschützten Wartungsdatei zurücksetzen.

## Entwicklung

Voraussetzung: .NET SDK 10 und Windows. Optional kann ein lokales SDK unter `.tools/dotnet` liegen; alternativ akzeptiert `build/Build.ps1` den Parameter `-Dotnet`.

### Repository-Struktur

```text
DriverPilot.slnx         Einstieg für IDE und dotnet build
src/DriverPilot/         WPF-Anwendung und Laufzeitcode
  AI/                   Codex-Anbindung und Antwortverarbeitung
  Hardware/             Inventarprofil und Herstellerprüfung
  Infrastructure/       Datenmodelle, lokaler Speicher, PowerShell-Aufrufe
  Monitoring/           Stabilitätsbeobachtung
  Reporting/            Bereinigter Systembericht und Freigabedialog
  Scripts/              Eingebettete Windows-Laufzeitskripte
  Security/             Paket- und Signaturprüfung
  UI/                   Hauptfenster, Ansichten und Icon-Loader
tests/                  Regressionen und Testskripte
  InApp/                In die App eingebundene isolierte Testmodi
contracts/              Sprachneutrale API-Verträge (Ferndiagnose v1, Entwurf)
build/                  Build, Paketierung und CI-Signierung
installer/              Windows-Setup und Inno-Setup-Definition
assets/                 Branding und öffentliches Zertifikat
docs/                   Release-Notizen, Sicherheit, Planung, Validierung
tools/                  Entwicklungswerkzeuge zur Asset-Erzeugung
artifacts/              Lokale Build-/Release-Ausgaben (nicht versioniert)
```

Die Quellstruktur ist bewusst vom **flachen Auslieferungspaket** getrennt: `Setup.ps1`, `PackageTrust.cs`, Zertifikat und Laufzeitdateien bleiben im fertigen Paket direkt neben der EXE. Installations- und Sicherheitsverträge ändern sich dadurch nicht. Alte lokale Ausgaben sind nur archivierte Entwicklungsartefakte, keine neu freigegebenen Releases.

```
dotnet build DriverPilot.slnx -c Release
dotnet run --project tests/HardwareRouting/HardwareRouting.csproj
dotnet run --project tests/SystemReport/SystemReport.csproj -- --integration --ui
powershell -NoProfile -ExecutionPolicy Bypass -File .\build\Build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-Pipeline.ps1
```

Das portable Entwicklungspaket liegt unter `artifacts/release-<Version>/`; signierte Release-Dateien entstehen unter `artifacts/dist/`. `tests/AppIcon` prüft das native Icon einer optional übergebenen EXE. `tools/build_icon.py` regeneriert PNG und ICO aus `assets/branding/DriverPilot.svg`.

Die veröffentlichte EXE benötigt keine separat installierte .NET-Laufzeit. `DriverPilot.exe --self-test` prüft den echten Inventarscan, Benachrichtigungsregeln, 25 Fälle der Paketauswahl und Wartungsregeln sowie die Nachbeobachtung und rendert alle WPF-Seiten. Keine Treiber werden dabei installiert. Codex startet ausschließlich nach Benutzeraktion in der App; Hintergrundtests starten keinen App Server. Der Protokollhandshake wurde separat geprüft. Die Anmeldung und ein kompletter Installationslauf sind ohne Benutzeranmeldung beziehungsweise geeignetes Testpaket nicht Ende-zu-Ende getestet.

Quellen: https://learn.chatgpt.com/docs/app-server und https://learn.microsoft.com/en-us/windows/win32/wua_sdk/using-the-windows-update-agent-api


## Repository und Release

GitHub Actions baut und testet Pushes auf `main`, Pull Requests gegen `main`, Versionstags und manuell gestartete Läufe. Beliebige andere Branch-Pushes starten diesen Workflow nicht automatisch. Nach erfolgreichem signiertem Build-Job steht unter **Actions → Windows build and release** das Artefakt `windows-x64-release` 14 Tage zum Download bereit. Es enthält die Setup-EXE, das portable ZIP, `SHA256SUMS.txt`, `DriverPilot.cer` und `ZERTIFIKAT-ANLEITUNG.txt`. Die Veröffentlichung eines Tags setzt zusätzlich den erfolgreichen Upgrade-Job voraus.

| Laufart | Produktionssignierung und Ausgabe |
| --- | --- |
| PR, main-Push oder manueller Lauf mit `sign=false` | Unsigniertes Entwicklungsprogramm; keine Release-ZIPs/Installer und kein `windows-x64-release`. Sicherheitstests nutzen kurzlebige Testschlüssel. |
| Manueller Lauf auf `main` mit `sign=true` | Signierter Testbuild einschließlich Signatur-/Manipulations- und Installerprüfung; Download als Actions-Artefakt, **kein GitHub Release**. |
| Push eines passenden `v*`-Versionstags | Signierte Release-Dateien; Veröffentlichung erst nach erfolgreichen Build- und Upgrade-Jobs. |

Signierte Testbuilds bewusst über **Actions → Windows build and release → Run workflow → main → sign** starten. Auf einem anderen Branch wird auch bei `sign=true` kein Produktionsschlüssel verwendet. Testartefakte sind nicht mit einem veröffentlichten Release gleichzusetzen. Der separate Upgrade-Job verwendet immer einen kurzlebigen Testschlüssel; sein `app-upgrade-evidence`-Artefakt ist ebenfalls 14 Tage verfügbar, auch bei Fehlern.

Ein Push eines Versionstags veröffentlicht diese Dateien nach erfolgreichen Tests automatisch als GitHub Release. Nur nach ausdrücklicher Release-Freigabe einen **neuen**, zur Version in `src/DriverPilot/DriverPilot.csproj` passenden Tag `v<Major.Minor.Patch>` erstellen und pushen. `v0.3.4` ist bereits veröffentlicht und darf nicht neu gesetzt oder überschrieben werden.

Lokal ohne Schlüssel: `build/Build.ps1`, `tests/Test-PackageSecurity.ps1` und `tests/Test-Pipeline.ps1` ausführen. Für einen ausdrücklich autorisierten Release: `build/Build.ps1 -Sign`, danach `tests/Test-Pipeline.ps1` und `build/Build-Release.ps1 -Sign`. Unsigned Builds sind nur Entwicklungsprogramme, keine installierbaren oder updatefähigen Pakete. Für die Setup-EXE wird Inno Setup 6 benötigt. Der Installationstest läuft ausschließlich auf einer wegwerfbaren GitHub-Windows-Testmaschine. Dort werden Installation, Paketprüfsummen und Wartungsauftrag geprüft; der Auftrag wird anschließend deaktiviert. Der Hersteller-Onlinetest gehört wegen wechselnder externer Quellen nicht zur Release-Pipeline.

Die Setup-EXE installiert DriverPilot und richtet die Automatik ein. ZIP entpacken und `DriverPilot.exe` starten ermöglicht den portablen Einstieg.

Ab 0.3.2 werden Versionstag-Releases mit dem privaten Codesigning-Zertifikat **Christian Strube** signiert: Programm-EXE, eigene DLL, Setup-Skript und Setup-EXE erhalten SHA-256-Signaturen und Zeitstempel. **Seit 0.3.3** entsteht nach dem Signieren der Paketdateien das vollständige Paketmanifest samt abgesetzter RSA-SHA256-Signatur; anschließend werden ZIP und signierter Installer erstellt und die Release-Prüfsummen berechnet. Die Manifest-Signatur hat ausdrücklich keine Zeitstempel-Verlängerung: Das fest hinterlegte Zertifikat muss bei jeder Paketprüfung aktuell gültig sein. Die Pipeline prüft Signaturen und weist manipulierte Testkopien zurück.

**Für Freunde:** `DriverPilot.cer` und [ZERTIFIKAT-ANLEITUNG.txt](https://github.com/Madchristian/DriverPilot/blob/main/docs/security/ZERTIFIKAT-ANLEITUNG.txt) liegen sowohl im Release als auch im ZIP. Den Zertifikat-Fingerabdruck direkt mit Christian abgleichen, bevor Vertrauen eingerichtet wird. Das Setup importiert keine Zertifikate. Selbstsignierung beseitigt nicht automatisch SmartScreen-Warnungen oder Smart-App-Control-Blockaden. Das private Repository erfordert auch zum Download der Releases eine Zugriffsberechtigung.

Der private Schlüssel liegt im persönlichen Windows-Zertifikatspeicher und verschlüsselt in GitHub Actions Secrets; zusätzlich ist ein separat passwortgeschütztes Offline-Backup mit Wiederherstellungsprobe erforderlich. Er wird nur bei Tag-Pushes oder ausdrücklich gestarteten signierten Testbuilds auf `main` in einen wegwerfbaren Runner importiert und vor dem Artefakt-Upload entfernt. Pull Requests und normale Branch-Builds erhalten keinen Schlüssel. Sie testen mit kurzlebigen Testschlüsseln, bauen nur Entwicklungsprogramme und erzeugen keine Installer oder Release-ZIPs. Es gibt keinen Testschlüssel-Schalter im produktiven Verifier. Ein manueller Lauf mit `sign=true` prüft die komplette Signierung, veröffentlicht aber kein Release. Wer Schreibrechte auf Repository/Workflows erhält, kann prinzipiell die Nutzung der Secrets beeinflussen. Für Freunde genügt es, die fertigen Release-Dateien weiterzugeben; dafür sind keine Schreibrechte am Repository nötig.

**Für Maintainer:** Das [Signing-Lifecycle-Runbook](https://github.com/Madchristian/DriverPilot/blob/main/docs/security/SIGNING-LIFECYCLE.md) beschreibt verschlüsseltes Offline-Backup und Restore, die 90-Tage-Warnung und den wöchentlichen öffentlichen Zertifikatsmonitor, den Wechsel des fest gepinnten Updaters sowie Release-Stopp und manuelle Vertrauensentfernung bei Schlüsselverlust oder Kompromittierung. Neues Windows-Vertrauen allein rotiert den Updater-Pin nicht. App und Installer importieren keine Zertifikate automatisch; es gibt keinen zentralen Widerruf der selbstsignierten Schlüssel.

Privat: https://github.com/Madchristian/DriverPilot . Quellcode und Dokumentation werden versioniert. Lokale SDKs, Buildordner, Diagnose-Snapshots, Authentifizierungsdaten und persönliche App-Einstellungen gehören nicht ins Repository.

**Inhalt des flachen Release-ZIPs ab 0.3.3:** `DriverPilot.exe`, `DriverPilot.dll`, die mitgelieferten .NET-/Windows-x64-Laufzeitdateien, `Setup.ps1`, `PackageTrust.cs`, `DriverPilot.cer`, `ZERTIFIKAT-ANLEITUNG.txt`, `UPDATE-SECURITY.md`, `README.md` sowie `package-manifest.json` und `package-manifest.sig`. Das Manifest listet sämtliche Paketdateien außer sich selbst und seiner Signatur auf. Kein privater Schlüssel, keine lokalen Testergebnisse oder Hardwarebilder; Setup-EXE und `SHA256SUMS.txt` sind separate Release-Dateien, nicht Teil des ZIPs.

`build/Build.ps1` erstellt das selbstständige Windows-x64-Paket inklusive SHA-256-Manifest. Tests auf einer separaten Kopie ausführen (`tests/Test-Pipeline.ps1` erledigt dies), niemals Diagnosedateien in ein Updatepaket schreiben: Das vollständige Inventar weist zusätzliche Dateien zurück. Die Tests prüfen lokal und teilweise online, installieren aber keine Treiber. Reale Treiberinstallationen, Rollbacks und BIOS-/Firmwareupdates wurden nicht zu Testzwecken durchgeführt.
