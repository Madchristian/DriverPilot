# Validierung 0.3.0 – 26.09.2026

- .NET 10 Windows-x64 Build/Publish erfolgreich.
- Echter Hardware-Inventarscan: 337 Geräte, 3 gruppierte Hinweise, keine Abfragewarnungen. Gerätecode 22 bleibt informativ. Ereignis 158 mit aktueller Datenträgerzuordnung dargestellt.
- 25 PowerShell-Prüffälle: bestehende Geräte-/Paketregeln, 14-Tage-Untergrenze, längere Beobachtung, Gerätesperre, Update-ID-Sperre, Tage, Fensterbeginn/-ende, Pause und ungültige Konfigurationsdateien. Echte Benutzereinstellungen wurden nicht verändert.
- Nachbeobachtung: Versionswechsel, bestehende Ereignisse, neue Wiederholung in bestehender Gruppe, Deduplizierung und Ablauf nach 24 Stunden geprüft.
- Paketprüfung: echte Dateiprüfsummen, manipulierte Prüfsumme, Pfadüberschreitung, leeres Manifest, falsches Produkt und erneute Installation derselben Version geprüft.
- Lokaler Transporttest: UTF-8 über alle drei Pipes, geteilte Mehrbytezeichen und vollständige 8-KB-Antwort bestanden. Kein Codex-/Git-Prozess dafür gestartet.
- Herstellerparser-/Quellentests bestanden; Liveprüfung weiterhin mit ausdrücklich offenen MSI-/Firmwarevergleichen. Zusätzlicher AMD-Grafik-Portalhinweis vorhanden.
- WPF: Übersicht 1280×850, zehn weitere Ansichten 1000×700 und Herstelleransicht gerendert. Wartungs- und Problemdarstellung visuell geprüft.
- Alle PowerShell-Dateien syntaktisch geprüft; Quellcode vor Upload auf gängige private Schlüssel-/Tokenpräfixe geprüft, keine Treffer.

Nicht Ende-zu-Ende getestet: echte Treiberinstallation und Rollback, BIOS-/Firmwareflash, zukünftiger Versionswechsel über die neue Updateauswahl sowie eine neue Live-Codex-Anmeldung. Diese Vorgänge wurden nicht als Test auf dem produktiven PC ausgelöst. Das Installationspaket ist nicht mit einem Herausgeberzertifikat signiert; SHA-256 erkennt Abweichungen vom Manifest, bestätigt aber keine unabhängige Herkunft.
