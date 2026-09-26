# DriverPilot 0.3.3

Release 0.3.3 sichert App-Updates mit signierten Manifesten und einem fest hinterlegten Herausgeber ab. Migration von veröffentlichten Versionen bis 0.3.2 nur über den unabhängig geprüften signierten Installer. Hinweise und Testgrenzen stehen im Repository unter `RELEASE-0.3.3.md`.

Windows-App für lokale Hardwarediagnose, Geräte-/Treiberüberwachung und konservative Treiberwartung. Für einen selbst zusammengestellten Windows-11-Gaming-PC mit Schwerpunkt Stabilität.

## Start

`DriverPilot.exe` öffnen. Geräte und ausgewählte Windows-Systemereignisse werden gelesen. Schließen minimiert in den Infobereich; dort lässt sich die App beenden. Die Diagnose läuft ohne Administratorrechte.

Die erste Installation erfolgt ausschließlich über den unabhängig geprüften, signierten Installer (siehe [UPDATE-SECURITY.md](UPDATE-SECURITY.md)). Er kopiert die App nach `C:\Program Files\DriverPilot`, legt geschützte Wartungsdaten unter `C:\ProgramData\DriverPilot` an und registriert `DriverPilot Maintenance`. **Einstellungen → Automatik einrichten** kann dort die Einrichtung mit Windows-Administratorabfrage erneut vornehmen. Portable Builds dürfen keine Skripte erhöht starten. Die Einrichtung ist für ein lokales Administratorkonto vorgesehen. Kein SYSTEM-Dienst und keine fremden Administrator-Anmeldedaten verwenden.

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

### In Entwicklung: fremde PCs und Systembericht

Der Arbeitsstand erweitert die Hardwareerfassung um Systemhersteller/-modell, Mainboardhersteller/-modell/-revision, BIOS-Datum, Architektur und Windows-Build. ASUS, MSI, Gigabyte, ASRock, Dell, HP, Lenovo und Acer werden über gemeinsame Herstelleradapter zugeordnet. Bei OEM-Rechnern und Notebooks hat der Systemhersteller Vorrang. Unbekannte oder unvollständige Zuordnungen bleiben ausdrücklich offen.

Neue Herstellerwege sind **manuelle offizielle Portale**, keine zugesagte modellgenaue Unterstützung. Es liegen noch keine bestätigten ASUS-/Freundes-Konfigurationen vor. Der bekannte MSI-/X670E-Pfad bleibt eng an die verifizierte Modellzuordnung gebunden; automatische Windows-11-Paketvergleiche setzen eine bestätigte x64-/Windows-11-Identität voraus. BIOS und Firmware werden nicht geflasht.

Unter **Hardware & Treiber → Systembericht teilen** lässt sich nach einem erfolgreichen frischen Scan eine lokale Vorschau öffnen. Der Bericht enthält ausschließlich ausgewählte strukturierte Hardwarefelder, App-/Windows-Version, relevante Geräteklassen mit bereinigten PCI-/USB-Modellkennungen und optionalen Benutzerangaben. GPU-Zuordnung erfolgt über die Modellkennung statt über frei formulierte Gerätenamen. Rohe Ereignisse, Chat, Einstellungen und vollständige Geräteinstanz-IDs werden nicht exportiert. Die Filter sind konservative Heuristiken, keine Garantie perfekter Anonymität: Vorschau und eigene Angaben vor dem Teilen vollständig prüfen.

- **Lokal speichern** funktioniert ohne Netzwerk und GitHub-Konto. Die Datei enthält exakt den Vorschautext; der Speicherort wird ausgewählt.
- **GitHub-Entwurf öffnen** erfordert eine bewusste Zustimmung und öffnet nur einen vorausgefüllten Browserentwurf. Das Repository ist privat; GitHub-Konto und Zugriff sind erforderlich. Das Issue veröffentlicht der Nutzer selbst.
- Bereits die Entwurfs-URL übermittelt den Bericht an Browser/GitHub und kann im Browserverlauf stehen. Kein automatischer Upload, eingebauter Token oder Clipboard-Zugriff.
- Entwurfs-URLs über 2.000 Zeichen werden nicht geöffnet oder abgeschnitten. In diesem Fall die vollständige Datei speichern und bewusst manuell weitergeben.
- Fehlende, fehlgeschlagene oder über 15 Minuten alte Scans müssen erneuert werden. Herstellerprüfungen werden nicht aus fremden/veralteten gespeicherten Berichten übernommen.

Die Funktion ersetzt nicht den umfassenderen Diagnoseexport mit begrenzten Protokollen aus Issue #7. Echte Freundesprofile und deren praktische Abnahme bleiben für Issue #10 erforderlich. Dieser Arbeitsstand ist noch kein veröffentlichtes Release.

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
- **App-Updates:** Die geschützte installierte App akzeptiert nur Pakete mit RSA-SHA256-signiertem Manifest vom fest hinterlegten Herausgeberzertifikat. Produkt, exakte Version, vollständiges Dateiinventar und SHA-256-Hashes sind authentifiziert. Die Administratorabfrage ist an den SHA-256-Wert des ausdrücklich genehmigten Manifests gebunden. Der bereits installierte Updater prüft erneut und prüft nochmals die geschützte Zwischenkopie; kein Kandidatenskript wird gestartet. Austausch erfolgt als vollständiger Ordner, nicht als Datei-Overlay. Der vorige Ordner bleibt als `Program Files/DriverPilot.previous-*` erhalten. App-Neustart erfolgt als normaler Benutzer; kein Windows-Neustart. **Migration von 0.3.2 und älter nur mit unabhängig geprüftem signiertem Installer**, nicht mit deren altem Updatepfad. Details und Grenzen: [UPDATE-SECURITY.md](UPDATE-SECURITY.md).

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
dotnet run --project tests/HardwareRouting/HardwareRouting.csproj
dotnet run --project tests/SystemReport/SystemReport.csproj -- --integration --ui
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
```

Die veröffentlichte EXE benötigt keine separat installierte .NET-Laufzeit. `DriverPilot.exe --self-test` prüft den echten Inventarscan, Benachrichtigungsregeln, 25 Fälle der Paketauswahl und Wartungsregeln sowie die Nachbeobachtung und rendert alle WPF-Seiten. Keine Treiber werden dabei installiert. Codex startet ausschließlich nach Benutzeraktion in der App; Hintergrundtests starten keinen App Server. Der Protokollhandshake wurde separat geprüft. Die Anmeldung und ein kompletter Installationslauf sind ohne Benutzeranmeldung beziehungsweise geeignetes Testpaket nicht Ende-zu-Ende getestet.

Quellen: https://learn.chatgpt.com/docs/app-server und https://learn.microsoft.com/en-us/windows/win32/wua_sdk/using-the-windows-update-agent-api


## Repository und Release

GitHub Actions baut und testet jeden Push auf `main`, Pull Requests sowie manuell gestartete Läufe. Nur für signierte Läufe steht unter **Actions → Windows build and release** das Artefakt `windows-x64-release` 14 Tage zum Download bereit. Es enthält die Setup-EXE, das portable ZIP und `SHA256SUMS.txt`.

Ein Push eines Versionstags veröffentlicht diese Dateien nach erfolgreichen Tests automatisch als GitHub Release. Beispiel für Version 0.3.3 (muss mit `DriverPilot.csproj` übereinstimmen):

```powershell
git tag v0.3.3
git push origin v0.3.3
```

Lokal ohne Schlüssel: `Build.ps1`, `tests/Test-PackageSecurity.ps1` und `Test-Pipeline.ps1` ausführen. Für einen ausdrücklich autorisierten Release: `Build.ps1 -Sign`, danach `Test-Pipeline.ps1` und `Build-Release.ps1 -Sign`. Unsigned Builds sind nur Entwicklungsprogramme, keine installierbaren oder updatefähigen Pakete. Für die Setup-EXE wird Inno Setup 6 benötigt. Der Installationstest läuft ausschließlich auf einer wegwerfbaren GitHub-Windows-Testmaschine. Dort werden Installation, Paketprüfsummen und Wartungsauftrag geprüft; der Auftrag wird anschließend deaktiviert. Der Hersteller-Onlinetest gehört wegen wechselnder externer Quellen nicht zur Release-Pipeline.

Die Setup-EXE installiert DriverPilot und richtet die Automatik ein. ZIP entpacken und `DriverPilot.exe` starten ermöglicht den portablen Einstieg.

Ab 0.3.2 werden Versionstag-Releases mit dem privaten Codesigning-Zertifikat **Christian Strube** signiert: Programm-EXE, eigene DLL, Setup-Skript und Setup-EXE erhalten SHA-256-Signaturen und Zeitstempel. Erst danach entstehen Paketmanifest und dessen abgesetzte RSA-SHA256-Signatur sowie Release-Prüfsummen. Die Manifest-Signatur hat ausdrücklich keine Zeitstempel-Verlängerung: Das fest hinterlegte Zertifikat muss bei jeder Paketprüfung aktuell gültig sein. Die Pipeline prüft Signaturen und weist manipulierte Testkopien zurück.

**Für Freunde:** `DriverPilot.cer` und [ZERTIFIKAT-ANLEITUNG.txt](ZERTIFIKAT-ANLEITUNG.txt) liegen sowohl im Release als auch im ZIP. Den Zertifikat-Fingerabdruck direkt mit Christian abgleichen, bevor Vertrauen eingerichtet wird. Das Setup importiert keine Zertifikate. Selbstsignierung beseitigt nicht automatisch SmartScreen-Warnungen oder Smart-App-Control-Blockaden. Das private Repository erfordert auch zum Download der Releases eine Zugriffsberechtigung.

Der private Schlüssel liegt ausschließlich im persönlichen Windows-Zertifikatspeicher und verschlüsselt in GitHub Actions Secrets. Er wird nur bei Tag-Pushes oder ausdrücklich gestarteten signierten Testbuilds auf `main` in einen wegwerfbaren Runner importiert und vor dem Artefakt-Upload entfernt. Pull Requests und normale Branch-Builds erhalten keinen Schlüssel. Sie testen mit kurzlebigen Testschlüsseln, bauen nur Entwicklungsprogramme und erzeugen keine Installer oder Release-ZIPs. Es gibt keinen Testschlüssel-Schalter im produktiven Verifier. Ein manueller Lauf mit `sign=true` prüft die komplette Signierung, veröffentlicht aber kein Release. Wer Schreibrechte auf Repository/Workflows erhält, kann prinzipiell die Nutzung der Secrets beeinflussen. Für Freunde genügt es, die fertigen Release-Dateien weiterzugeben; dafür sind keine Schreibrechte am Repository nötig.

Privat: https://github.com/Madchristian/DriverPilot . Quellcode und Dokumentation werden versioniert. Lokale SDKs, Buildordner, Diagnose-Snapshots, Authentifizierungsdaten und persönliche App-Einstellungen gehören nicht ins Repository. Das Release-ZIP enthält nur das authentifizierte Inventar plus Manifest und abgesetzte Signatur; keine lokalen Testergebnisse oder Hardwarebilder.

`Build.ps1` erstellt das selbstständige Windows-x64-Paket inklusive SHA-256-Manifest. Tests auf einer separaten Kopie ausführen (`Test-Pipeline.ps1` erledigt dies), niemals Diagnosedateien in ein Updatepaket schreiben: Das vollständige Inventar weist zusätzliche Dateien zurück. Die Tests prüfen lokal und teilweise online, installieren aber keine Treiber. Reale Treiberinstallationen, Rollbacks und BIOS-/Firmwareupdates wurden nicht zu Testzwecken durchgeführt.
