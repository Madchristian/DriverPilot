# DriverPilot 0.3

Windows-App für lokale Hardwarediagnose, Geräte-/Treiberüberwachung und konservative Treiberwartung. Für einen selbst zusammengestellten Windows-11-Gaming-PC mit Schwerpunkt Stabilität.

## Start

`DriverPilot.exe` öffnen. Geräte und ausgewählte Windows-Systemereignisse werden gelesen. Schließen minimiert in den Infobereich; dort lässt sich die App beenden. Die Diagnose läuft ohne Administratorrechte.

Unter **Einstellungen → Automatik einrichten** wird einmalig die Windows-Administratorabfrage angezeigt. Das Setup kopiert die veröffentlichte App nach `C:\Program Files\DriverPilot`, legt geschützte Wartungsdaten unter `C:\ProgramData\DriverPilot` an und registriert `DriverPilot Maintenance` für den angemeldeten Benutzer mit erhöhten Rechten. Die Einrichtung ist für ein lokales Administratorkonto vorgesehen. Kein SYSTEM-Dienst und keine fremden Administrator-Anmeldedaten verwenden.

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

Der zusätzliche Bereich prüft unterstützte offizielle Herstellerquellen täglich, solange die Überwachung im Infobereich läuft, oder über „Herstellerquellen prüfen“. Codex kann dieselbe Prüfung mit `driverpilot_manufacturer_updates` aufrufen. Hardwaremodelle werden lokal erkannt; keine Seriennummern werden benötigt.

- **AMD X670E:** vergleicht das offizielle Chipsatzpaket mit der tatsächlich installierten Paketversion aus der Windows-Programmliste. Einzelne Chipsatztreiberversionen werden nicht mit der Paketversion vermischt.
- **NVIDIA:** ermittelt das exakte GPU-Modell und Windows 11 über den Katalog der offiziellen NVIDIA-Webseite. Vergleich mit dem Game-Ready-WHQL-Paket, ohne Beta-/Vorabversionen. Die Windows-Treiberversion wird in das NVIDIA-Schema umgerechnet.
- **MSI MPG X670E CARBON WIFI:** erfasst BIOS-Version und Datum sowie zugehörige LAN-/WLAN-/Bluetooth-Treiber. Ruft die genaue MSI-Supportseite ab. Diese blockiert derzeit automatisierte Zugriffe (HTTP 403); deshalb wird kein erfundener BIOS-Versionsvergleich angezeigt. „Offizielle Quelle öffnen“ führt zur manuellen Prüfung im Browser.
- **WD_BLACK SN850X:** zeigt die lokale Firmware. Die korrekte Firmwareprüfung erfolgt über SANDISK Dashboard, da ein Modell verschiedene Firmwarezweige haben kann. Kein öffentlicher Versionsvergleich wird vorgetäuscht.
- **Corsair MP600 / MP600 PRO LPX:** zeigt die lokale Firmware und verlinkt die offizielle Anleitung zur SSD Toolbox. Die neue Firmware muss dort für das konkrete Laufwerk geprüft werden.
- **Creative ZxR:** prüft, ob der offizielle Katalog das passende Treiberpaket enthält. Ohne eindeutig vergleichbare Versionsdaten bleibt der Vergleich ausdrücklich offen.

Neue belegte Treiberversionen können eine Benachrichtigung auslösen. Ein offener Vergleich oder eine gesperrte Quelle bedeutet nicht „aktuell“. Datum und Quelle sind sichtbar, ein älterer zwischengespeicherter Bericht ist mit seinem Prüfzeitpunkt gekennzeichnet. BIOS, SSD-Firmware und Hersteller-EXE-Pakete werden durch diese Suche weder heruntergeladen noch installiert; die bestehende Windows-Update-Automatik wird nicht erweitert.

## Neu in 0.3

- **Wartung & Sperren:** Wochentage, Stunden, 14–90 Tage Paketbeobachtung, Pause bis morgen, eigene Spiele-/Streaming-Prozessnamen und Netzwerklastgrenze. Der erhöhte Worker liest die Regeln erneut vor jedem Paket und unmittelbar vor der Installation. Ungültige Regeln verhindern die Wartung. Eine laufende Installation wird nicht abgebrochen.
- **Hardware → Gerät auf Version halten:** verhindert DriverPilot-Updates für dieses konkrete Geräteexemplar. **Treiberupdates → Update sperren:** blockiert das ausgewählte Windows-Update-Paket über seine Update-ID (einschließlich Revisionen). Sperren lassen sich unter Wartung aufheben. Sie verändern keine Richtlinien von Windows Update oder Hersteller-Tools.
- **Nach Update beobachten:** vergleicht den letzten gespeicherten Gerätestand mit dem neuen Scan, erkennt Versionsänderungen und sammelt 24 Stunden ab Erkennung zusätzliche Meldungen. Auch wiederholte Ereignisse in bereits bekannten Gruppen werden berücksichtigt. Benachrichtigung bei zusätzlichen Befunden. Beobachtung nur während laufender GUI; ein spät erkannter Wechsel hat keinen rückwirkend vollständigen Verlauf. Die Ansicht beweist keine Kausalität oder Fehlerfreiheit.
- **Diagnose:** Systemereignisse nach Quelle, Ereignis-ID, verfügbarer Gerätezuordnung und Stunde gruppieren; WLAN-Abbrüche ergänzen, sofern das optionale Protokoll aktiv ist. Disk 158 mit aktueller Win32_DiskDrive-Zuordnung; Datenträgernummern können sich ändern. Nicht jede Ereignisquelle enthält eine Geräte-ID. Keine vollständige Netzwerk- oder Crashdumpüberwachung.
- **Wiederherstellung:** vorhandene INF-Sicherungen auflisten, Ordner und Geräte-Manager öffnen, unterstützten manuellen Rückweg erklären. Zusätzlich dokumentiert der Worker Treiberversionen/INF-Namen vor und nach jeder Installation unter `ProgramData/DriverPilot/Audit`. Rückrollen ist von Windows und vom Treiber abhängig.
- **Hersteller:** zusätzliche manuelle Portale für AMD-Grafik und Intel sowie SSD-Hinweise für Samsung und Crucial. Diese sind ausdrücklich keine automatischen Versionsvergleiche. Geführte Prüfung pro Eintrag mit Modellabgleich, Herstelleranleitung, Vorbereitung und gespeicherter Benutzernotiz. MSI-M-FLASH: https://www.msi.com/support/technical_details/MB_BIOS_Update ; Samsung: https://semiconductor.samsung.com/consumer-storage/support/tools/ ; Intel: https://www.intel.com/content/www/us/en/download-center/home.htm .
- **Codex:** ausgewählten Befund datensparsam zum Erklären vorbereiten; Benutzer sendet ihn selbst. Anweisungen trennen lokale Fakten, Herstellerangaben und Hypothesen. Lokaler Chat wird gespeichert (maximal 200.000 Zeichen), kann gelöscht werden; nach App-Neustart wird die Anzeige wiederhergestellt, die neue KI-Sitzung kennt ältere Gespräche nicht automatisch. Tool-Protokoll enthält Start, Erfolg/Fehler und tatsächliches Ergebnis (maximal 100 Einträge, 20.000 Zeichen pro Ergebnis).
- **App-Updates:** Einstellungen öffnen private GitHub-Releases und nehmen ein entpacktes lokales Paket entgegen. SHA-256-Manifest, Produkt, Version und Dateinamen werden vorab und beim erhöhten Installieren geprüft; Bytes werden erst in einen geschützten Zwischenordner kopiert. Das Manifest ist **keine Herausgebersignatur**. Nur eigene Builds oder Releases des privaten Projekts verwenden. Vorherige App-Dateien bleiben unter ProgramData erhalten. App-Neustart erfolgt als normaler Benutzer; kein Windows-Neustart.

## Weitere Grenzen

Automatische Herstellerdownloads außerhalb Windows Update, KI-gestützte Onlineprüfung bekannter Versionsprobleme, automatisches Rollback, Crashdumpanalyse und ein vollständiger Windows-Dienst sind noch nicht implementiert. Nicht jede Hardware hat bereits einen Herstelleradapter. Die App behauptet daher nicht, sämtliche Treiber als aktuell oder ein System als stabil nachgewiesen zu haben. Die KI benötigt ein passendes ChatGPT-/Codex-Konto sowie eine installierte offizielle Codex CLI. Kontolimits gelten weiterhin.

Das überwachte WLAN kann absichtlich deaktiviert sein; Code 22 wird als Hinweis behandelt. Ein Ereignis beweist keinen Treiberdefekt. Eine zeitliche Häufung beweist keine Ursache.

## Daten und Betrieb

Benutzereinstellungen, Diagnose-Snapshot und maximal 500 Verlaufseinträge: `%LOCALAPPDATA%\DriverPilot`. Wartungsstatus, Paketbeobachtung und maximal 100 Installationsresultate: `%PROGRAMDATA%\DriverPilot`. Windows-Benachrichtigungen hängen von den Windows-Benachrichtigungseinstellungen ab. Die GUI muss für Benachrichtigungen im Infobereich laufen. Der Wartungsauftrag benötigt eine angemeldete Sitzung.

Die Automatik lässt sich über Einstellungen deaktivieren. Eine verriegelte fehlerhafte/unterbrochene Installation wird nicht automatisch erneut versucht. Erst nach manueller Prüfung darf ein Administrator den `Pending`-Eintrag in der geschützten Wartungsdatei zurücksetzen.

## Entwicklung

Voraussetzung: .NET SDK 10, Windows. Ein lokales SDK liegt für diese Entwicklung unter `.tools/dotnet`.

```
dotnet build -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
```

Die veröffentlichte EXE benötigt keine separat installierte .NET-Laufzeit. `DriverPilot.exe --self-test` prüft den echten Inventarscan, Benachrichtigungsregeln, 25 Fälle der Paketauswahl und Wartungsregeln sowie die Nachbeobachtung und rendert alle WPF-Seiten. Keine Treiber werden dabei installiert. Codex startet ausschließlich nach Benutzeraktion in der App; Hintergrundtests starten keinen App Server. Der Protokollhandshake wurde separat geprüft. Die Anmeldung und ein kompletter Installationslauf sind ohne Benutzeranmeldung beziehungsweise geeignetes Testpaket nicht Ende-zu-Ende getestet.

Quellen: https://learn.chatgpt.com/docs/app-server und https://learn.microsoft.com/en-us/windows/win32/wua_sdk/using-the-windows-update-agent-api


## Repository und Release

GitHub Actions baut und testet jeden Push auf `main`, Pull Requests sowie manuell gestartete Läufe. Unter **Actions → Windows build and release** steht das Artefakt `windows-x64-release` 14 Tage zum Download bereit. Es enthält die Setup-EXE, das portable ZIP und `SHA256SUMS.txt`.

Ein Push eines Versionstags veröffentlicht diese Dateien nach erfolgreichen Tests automatisch als GitHub Release. Beispiel für Version 0.3.1 (muss mit `DriverPilot.csproj` übereinstimmen):

```powershell
git tag v0.3.1
git push origin v0.3.1
```

Lokal: zuerst `Build.ps1`, danach `Test-Pipeline.ps1` und `Build-Release.ps1` ausführen. Für die Setup-EXE wird Inno Setup 6 benötigt. Der Installationstest läuft ausschließlich auf einer wegwerfbaren GitHub-Windows-Testmaschine. Dort werden Installation, Paketprüfsummen und Wartungsauftrag geprüft; der Auftrag wird anschließend deaktiviert. Der Hersteller-Onlinetest gehört wegen wechselnder externer Quellen nicht zur Release-Pipeline.

Die Setup-EXE installiert DriverPilot und richtet die Automatik ein. ZIP entpacken und `DriverPilot.exe` starten ermöglicht den portablen Einstieg.

Ab 0.3.2 werden Versionstag-Releases mit dem privaten Codesigning-Zertifikat **DriverPilot (Christian)** signiert: Programm-EXE, eigene DLL, Setup-Skript und Setup-EXE erhalten SHA-256-Signaturen und Zeitstempel. Erst danach entstehen Paketmanifest und Release-Prüfsummen. Die Pipeline prüft Signaturen und weist manipulierte Testkopien zurück.

**Für Freunde:** `DriverPilot.cer` und [ZERTIFIKAT-ANLEITUNG.txt](ZERTIFIKAT-ANLEITUNG.txt) liegen sowohl im Release als auch im ZIP. Den Zertifikat-Fingerabdruck direkt mit Christian abgleichen, bevor Vertrauen eingerichtet wird. Das Setup importiert keine Zertifikate. Selbstsignierung beseitigt nicht automatisch SmartScreen-Warnungen oder Smart-App-Control-Blockaden. Das private Repository erfordert auch zum Download der Releases eine Zugriffsberechtigung.

Der private Schlüssel liegt ausschließlich im persönlichen Windows-Zertifikatspeicher und verschlüsselt in GitHub Actions Secrets. Er wird nur bei Tag-Pushes oder ausdrücklich gestarteten signierten Testbuilds auf `main` in einen wegwerfbaren Runner importiert und vor dem Artefakt-Upload entfernt. Pull Requests und normale Branch-Builds erhalten keinen Schlüssel und erzeugen unsignierte Testpakete. Ein manueller Lauf mit `sign=true` prüft die komplette Signierung, veröffentlicht aber kein Release. Wer Schreibrechte auf Repository/Workflows erhält, kann prinzipiell die Nutzung der Secrets beeinflussen. Für Freunde genügt es, die fertigen Release-Dateien weiterzugeben; dafür sind keine Schreibrechte am Repository nötig.

Privat: https://github.com/Madchristian/DriverPilot . Quellcode und Dokumentation werden versioniert. Lokale SDKs, Buildordner, Diagnose-Snapshots, Authentifizierungsdaten und persönliche App-Einstellungen gehören nicht ins Repository. Das Release-ZIP enthält nur die Paketmanifest-Dateien und README; keine lokalen Testergebnisse oder Hardwarebilder.

`Build.ps1` erstellt das selbstständige Windows-x64-Paket inklusive SHA-256-Manifest. Danach die EXE mit `--self-test`, `--transport-test` und `--manufacturer-test` ausführen. Die Tests prüfen lokal und teilweise online, installieren aber keine Treiber. Reale Treiberinstallationen, Rollbacks und BIOS-/Firmwareupdates wurden nicht zu Testzwecken durchgeführt.
