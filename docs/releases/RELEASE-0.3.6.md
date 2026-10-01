# DriverPilot 0.3.6 — persönlicher Testkandidat

**Vorbereitet, nicht veröffentlicht. Keine Produktions- oder Pilotfreigabe.** Die neue Versionsnummer unterscheidet diesen Quellstand vom älteren signierten 0.3.5-Testinstaller; dessen Artefakte werden nicht ersetzt. Die veröffentlichte Version bleibt 0.3.4.

## Änderungen gegenüber dem 0.3.5-Testkandidaten

- **Hilfe von Christian:** dunkle, lesbare Auswahlfelder einschließlich geöffneter Liste und Tastaturfokus, unabhängig vom hellen Windows-Standardtemplate.
- Zustimmung, „1 · Endgültiges JSON vorbereiten“, explizites Senden und lokaler Export bleiben außerhalb des scrollenden Inhalts erreichbar. Die Fenstergröße wird auf den Monitorarbeitsbereich in WPF-DIPs begrenzt. Die zugehörigen isolierten Screenshots wurden vom Benutzer visuell akzeptiert.
- Vorschau, `consent`, exakte UTF-8-Export-/Request-Identität, Retry-Key und Freigabeentzug bleiben unverändert abgesichert. Kein neues Wire-Schema und kein `driverpilot:`-Protokollhandler.
- Seit der früheren Kandidatenvorbereitung auf `main` zusammengeführte Absicherung: unvollständige gespeicherte Snapshots werden abgewiesen; Deinstallations-Recovery-Receipt mit zweiphasiger Vorprüfung und erneuter vollständiger Pin-/Planvalidierung vor dem Löschen. Diese Änderungen ersetzen keinen erneuten signierten CI-Lebenszyklustest des neuen Kandidaten.

## Prüfung und Artefakte

Die isolierten Branding-, Systembericht-, Ferndiagnose- und Transparenztests werden in Debug und Release ausgeführt. Transparenztests verwenden das produktive Dialogfenster mit einer einfachen WPF-Application, synthetischem Scan, In-Memory-Zugang und Fake-Transport, nicht das produktive App-/MainWindow-Startup. Sie prüfen auch Kontrast, Tastaturfokus, den festen Aktionsbereich bei Standard-/Mindestgröße und simulierte DIP-Arbeitsbereiche. Das ist kein physischer DPI-Wechseltest; der Hover-Trigger wird synthetisch aktiviert.

`build/Build.ps1` **ohne** `-Sign` erzeugt `artifacts/release-0.3.6/`: selbstständiger Windows-x64-Entwicklungsbuild, **kein Installer, keine Produktionssignatur, kein authentifiziertes Updatepaket**. Isolierte veröffentlichte Testharnesses und Scratch-Kopien dürfen diesen Build prüfen, ohne die normale Anwendung zu starten. Befehle, Ergebnisse, Screenshots und Byte-Hashes werden im separaten Kandidatennachweis festgehalten; diese Beschreibung allein behauptet keinen Testerfolg.

## Offene Freigaben

- Drei frische unabhängige Reviews müssen denselben endgültigen eingefrorenen Quellstand prüfen; frühere Reviews gelten nicht automatisch weiter.
- Der aktuelle Live-Datenschutzhinweis des Servers ist noch ein Entwurf. Autorisierter echter Windows-Client/Server-E2E (Pairing, bewusstes Senden, Freigabe/Ergebnis, Feedback und Löschen) steht noch aus. Synthetische Clienttests oder Serverberichte sind keine vollständige Pilotabnahme.
- Keine lokale Signierung, Installation, Zertifikatsänderung, Wartungsregistrierung oder Treiberinstallation im Rahmen dieser Vorbereitung. Kein Commit, Push, Tag oder Release. Ein neuer signierter CI-Testinstaller und jede Veröffentlichung benötigen die jeweiligen gesonderten Freigaben.
